using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Npgsql;
using WslPostgreTool.Models;

namespace WslPostgreTool.Services;

/// <summary>
/// 高性能データ比較サービス（.NET 9 最適化）
/// </summary>
public class CompareService
{
    private const int BatchSize = 10000; // バッチサイズ

    /// <summary>
    /// 2つのデータベースを比較
    /// </summary>
    public async Task<List<RowComparisonResult>> CompareDatabasesAsync(
        string oldConnectionString,
        string newConnectionString,
        string schemaName,
        string tableName,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        var results = new List<RowComparisonResult>();

        // 主キー列を取得
        var dbService = new DatabaseService();
        var primaryKeys = await dbService.GetPrimaryKeyColumnsAsync(oldConnectionString, schemaName, tableName);
        
        if (primaryKeys.Count == 0)
        {
            progress?.Report((0, 0, $"テーブル '{schemaName}.{tableName}' に主キーがありません。スキップします。"));
            return results;
        }

        progress?.Report((0, 0, $"テーブル '{schemaName}.{tableName}' の比較を開始しています..."));

        // 旧データベースのデータを読み込み（バッチ処理）
        var oldData = await LoadDataWithHashAsync(oldConnectionString, schemaName, tableName, primaryKeys, progress, "旧");
        
        // 新データベースのデータを読み込み（バッチ処理）
        var newData = await LoadDataWithHashAsync(newConnectionString, schemaName, tableName, primaryKeys, progress, "新");

        progress?.Report((0, 0, "データ比較を実行しています..."));

        // FrozenDictionary を使用して高性能な比較（.NET 9 最適化）
        var comparer = new PrimaryKeyComparer();
        var oldFrozen = oldData.ToFrozenDictionary(comparer);
        var newFrozen = newData.ToFrozenDictionary(comparer);

        // 削除された行を検出（旧にあり、新にない）
        foreach (var kvp in oldFrozen)
        {
            if (!newFrozen.ContainsKey(kvp.Key))
            {
                results.Add(new RowComparisonResult
                {
                    TableName = $"{schemaName}.{tableName}",
                    Status = ComparisonStatus.Deleted,
                    PrimaryKeyValues = kvp.Key,
                    OldValues = kvp.Value.Values
                });
            }
        }

        // 追加・更新された行を検出
        foreach (var kvp in newFrozen)
        {
            if (!oldFrozen.TryGetValue(kvp.Key, out var oldRow))
            {
                // 追加
                results.Add(new RowComparisonResult
                {
                    TableName = $"{schemaName}.{tableName}",
                    Status = ComparisonStatus.Added,
                    PrimaryKeyValues = kvp.Key,
                    NewValues = kvp.Value.Values
                });
            }
            else if (oldRow.Hash != kvp.Value.Hash)
            {
                // 更新
                results.Add(new RowComparisonResult
                {
                    TableName = $"{schemaName}.{tableName}",
                    Status = ComparisonStatus.Updated,
                    PrimaryKeyValues = kvp.Key,
                    OldValues = oldRow.Values,
                    NewValues = kvp.Value.Values
                });
            }
        }

        progress?.Report((results.Count, results.Count, $"比較完了: 削除={results.Count(r => r.Status == ComparisonStatus.Deleted)}, 追加={results.Count(r => r.Status == ComparisonStatus.Added)}, 更新={results.Count(r => r.Status == ComparisonStatus.Updated)}"));

        return results;
    }

    /// <summary>
    /// データをバッチで読み込み、ハッシュ値を計算（メモリ効率化）
    /// </summary>
    private async Task<Dictionary<Dictionary<string, object?>, RowData>> LoadDataWithHashAsync(
        string connectionString,
        string schemaName,
        string tableName,
        List<string> primaryKeys,
        IProgress<(int current, int total, string message)>? progress,
        string label)
    {
        var data = new Dictionary<Dictionary<string, object?>, RowData>(new PrimaryKeyComparer());
        var totalRows = 0L;
        var processedRows = 0L;

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // 総行数を取得
        var countSql = $"SELECT COUNT(*) FROM {schemaName}.{tableName}";
        await using var countCmd = new NpgsqlCommand(countSql, conn);
        totalRows = Convert.ToInt64(await countCmd.ExecuteScalarAsync());

        // 全列を取得
        var allColumns = await GetAllColumnsAsync(conn, schemaName, tableName);
        var nonPrimaryKeyColumns = allColumns.Except(primaryKeys).ToList();

        var selectSql = $"SELECT {string.Join(", ", allColumns.Select(c => $"\"{c}\""))} FROM {schemaName}.{tableName}";
        await using var cmd = new NpgsqlCommand(selectSql, conn);
        cmd.CommandTimeout = 0; // タイムアウト無制限

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);

        var batch = new List<(Dictionary<string, object?> pk, Dictionary<string, object?> values, long hash)>();

        while (await reader.ReadAsync())
        {
            // 主キー値を取得
            var primaryKeyValues = new Dictionary<string, object?>();
            foreach (var pk in primaryKeys)
            {
                var index = reader.GetOrdinal(pk);
                primaryKeyValues[pk] = reader.IsDBNull(index) ? null : reader.GetValue(index);
            }

            // 非主キー列の値を取得してハッシュ計算
            var nonPkValues = new Dictionary<string, object?>();
            var hashBuilder = new StringBuilder();

            foreach (var col in nonPrimaryKeyColumns)
            {
                var index = reader.GetOrdinal(col);
                var value = reader.IsDBNull(index) ? null : reader.GetValue(index);
                nonPkValues[col] = value;
                
                // ハッシュ計算用の文字列を構築
                hashBuilder.Append(col).Append('=');
                hashBuilder.Append(value?.ToString() ?? "NULL").Append('|');
            }

            // ハッシュ値を計算（.NET 9 の SHA256）
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashBuilder.ToString()));
            var hash = BitConverter.ToInt64(hashBytes, 0);

            batch.Add((primaryKeyValues, nonPkValues, hash));
            processedRows++;

            // バッチサイズに達したら処理
            if (batch.Count >= BatchSize)
            {
                ProcessBatch(batch, data);
                batch.Clear();
                
                var percentage = totalRows > 0 ? (int)(processedRows * 100 / totalRows) : 0;
                progress?.Report((percentage, 100, $"[{label}] {processedRows}/{totalRows} 行を処理しました ({percentage}%)"));
            }
        }

        // 残りのバッチを処理
        if (batch.Count > 0)
        {
            ProcessBatch(batch, data);
        }

        return data;
    }

    private static void ProcessBatch(
        List<(Dictionary<string, object?> pk, Dictionary<string, object?> values, long hash)> batch,
        Dictionary<Dictionary<string, object?>, RowData> data)
    {
        foreach (var (pk, values, hash) in batch)
        {
            data[pk] = new RowData { Values = values, Hash = hash };
        }
    }

    private async Task<List<string>> GetAllColumnsAsync(NpgsqlConnection conn, string schemaName, string tableName)
    {
        var columns = new List<string>();

        const string sql = @"
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = @schema
              AND table_name = @table
            ORDER BY ordinal_position";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("schema", schemaName);
        cmd.Parameters.AddWithValue("table", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private class RowData
    {
        public Dictionary<string, object?> Values { get; set; } = new();
        public long Hash { get; set; }
    }

    /// <summary>
    /// 主キーの比較子（Dictionary のキーとして使用）
    /// </summary>
    public class PrimaryKeyComparer : IEqualityComparer<Dictionary<string, object?>>
    {
        public bool Equals(Dictionary<string, object?>? x, Dictionary<string, object?>? y)
        {
            if (x == null || y == null) return x == y;
            if (x.Count != y.Count) return false;

            foreach (var kvp in x)
            {
                if (!y.TryGetValue(kvp.Key, out var value) || !Equals(kvp.Value, value))
                    return false;
            }

            return true;
        }

        public int GetHashCode(Dictionary<string, object?> obj)
        {
            var hash = new HashCode();
            foreach (var kvp in obj.OrderBy(k => k.Key))
            {
                hash.Add(kvp.Key);
                hash.Add(kvp.Value);
            }
            return hash.ToHashCode();
        }

        private static bool Equals(object? x, object? y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            return x.Equals(y);
        }
    }
}

