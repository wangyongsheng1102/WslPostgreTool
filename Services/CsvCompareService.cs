using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using WslPostgreTool.Models;

namespace WslPostgreTool.Services;

/// <summary>
/// CSV ファイル比較サービス
/// </summary>
public class CsvCompareService
{
    private const int BatchSize = 10000;

    private static CsvConfiguration CreateCsvConfiguration()
    {
        // 避免“按物理行读取”导致的错行：字段内含换行符时，必须依赖 CSV 解析器把记录拼回去。
        return new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            DetectDelimiter = false,
            Delimiter = ",",
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.None,
            BadDataFound = null,
            MissingFieldFound = null,
        };
    }

    private static async Task<long> CountCsvDataRecordsAsync(string csvPath)
    {
        var config = CreateCsvConfiguration();

        await using var stream = File.OpenRead(csvPath);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync())
        {
            return 0;
        }

        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? Array.Empty<string>();

        long count = 0;
        while (await csv.ReadAsync())
        {
            var record = csv.Parser.Record;
            if (record == null || record.Length != headers.Length)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// 2つのCSVファイルを比較（主キーはデータベースから取得、主キーがない場合は整行比較）
    /// </summary>
    public async Task<List<RowComparisonResult>> CompareCsvFilesAsync(
        string baseCsvPath,
        string compareCsvPath,
        List<string>? primaryKeyColumns,
        string connectionString,
        string schemaName,
        string tableName,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        var results = new List<RowComparisonResult>();

        progress?.Report((0, 0, $"CSV ファイルの比較を開始しています..."));

        // 主キーがない場合は整行比較
        bool useFullRowComparison = primaryKeyColumns == null || primaryKeyColumns.Count == 0;

        if (useFullRowComparison)
        {
            progress?.Report((0, 0, "主キーがないため、整行比較モードで実行します。"));
        }

        // Base CSVファイルのデータを読み込み
        var baseData = await LoadCsvDataWithHashAsync(
            baseCsvPath, 
            primaryKeyColumns ?? new List<string>(), 
            progress, 
            "Base",
            useFullRowComparison);
        
        // Compare CSVファイルのデータを読み込み
        var compareData = await LoadCsvDataWithHashAsync(
            compareCsvPath, 
            primaryKeyColumns ?? new List<string>(), 
            progress, 
            "Compare",
            useFullRowComparison);

        progress?.Report((0, 0, "データ比較を実行しています..."));

        // FrozenDictionary を使用して高性能な比較
        var comparer = new CompareService.PrimaryKeyComparer();
        var baseFrozen = baseData.ToFrozenDictionary(comparer);
        var compareFrozen = compareData.ToFrozenDictionary(comparer);

        // 削除された行を検出（BaseにあってCompareにない）
        foreach (var kvp in baseFrozen)
        {
            if (!compareFrozen.ContainsKey(kvp.Key))
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
        foreach (var kvp in compareFrozen)
        {
            if (!baseFrozen.TryGetValue(kvp.Key, out var baseRow))
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
            else if (baseRow.Hash != kvp.Value.Hash)
            {
                // 更新
                results.Add(new RowComparisonResult
                {
                    TableName = $"{schemaName}.{tableName}",
                    Status = ComparisonStatus.Updated,
                    PrimaryKeyValues = kvp.Key,
                    OldValues = baseRow.Values,
                    NewValues = kvp.Value.Values
                });
            }
        }

        progress?.Report((results.Count, results.Count, 
            $"比較完了: 削除={results.Count(r => r.Status == ComparisonStatus.Deleted)}, " +
            $"追加={results.Count(r => r.Status == ComparisonStatus.Added)}, " +
            $"更新={results.Count(r => r.Status == ComparisonStatus.Updated)}"));

        return results;
    }

    /// <summary>
    /// CSVファイルからデータを読み込み、ハッシュ値を計算
    /// </summary>
    private async Task<Dictionary<Dictionary<string, object?>, RowData>> LoadCsvDataWithHashAsync(
        string csvPath,
        List<string> primaryKeyColumns,
        IProgress<(int current, int total, string message)>? progress,
        string label,
        bool useFullRowComparison = false)
    {
        var data = new Dictionary<Dictionary<string, object?>, RowData>(new CompareService.PrimaryKeyComparer());
        
        if (!File.Exists(csvPath))
        {
            progress?.Report((0, 0, $"ファイルが見つかりません: {csvPath}"));
            return data;
        }

        var config = CreateCsvConfiguration();
        var totalRows = await CountCsvDataRecordsAsync(csvPath);
        var processedRows = 0L;

        List<int> primaryKeyIndices;
        List<int> nonPrimaryKeyIndices;

        await using var stream = File.OpenRead(csvPath);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync())
        {
            return data;
        }

        csv.ReadHeader();
        var headers = (csv.HeaderRecord ?? Array.Empty<string>()).ToList();

        if (headers.Count == 0)
        {
            return data;
        }

        if (useFullRowComparison)
        {
            // 整行比較：すべての列を主キーとして扱う
            primaryKeyIndices = Enumerable.Range(0, headers.Count).ToList();
            nonPrimaryKeyIndices = new List<int>(); // 非主キー列なし
        }
        else
        {
            // 主キー列のインデックスを取得
            primaryKeyIndices = primaryKeyColumns
                .Select(pk => headers.IndexOf(pk))
                .Where(idx => idx >= 0)
                .ToList();

            if (primaryKeyIndices.Count == 0)
            {
                progress?.Report((0, 0, $"主キー列が見つかりません: {string.Join(", ", primaryKeyColumns)}"));
                return data;
            }

            // 非主キー列のインデックス
            nonPrimaryKeyIndices = Enumerable.Range(0, headers.Count)
                .Where(i => !primaryKeyIndices.Contains(i))
                .ToList();
        }

        var batch = new List<(Dictionary<string, object?> pk, Dictionary<string, object?> values, long hash)>();

        // データ行を処理（字段内换行符也会被 CsvHelper 正确拼成同一条记录）
        while (await csv.ReadAsync())
        {
            var record = csv.Parser.Record;
            if (record == null || record.Length != headers.Count) continue; // 列数が一致しない場合はスキップ

            // 主キー値を取得
            var primaryKeyValues = new Dictionary<string, object?>();
            foreach (var idx in primaryKeyIndices)
            {
                var colName = headers[idx];
                var rawValue = csv.GetField<string>(idx);
                primaryKeyValues[colName] = string.IsNullOrEmpty(rawValue) ? null : rawValue;
            }

            // 非主キー列の値を取得してハッシュ計算
            var nonPkValues = new Dictionary<string, object?>();
            var hashBuilder = new StringBuilder();

            if (useFullRowComparison)
            {
                // 整行比較：すべての列の値をハッシュに含める
                foreach (var idx in primaryKeyIndices)
                {
                    var colName = headers[idx];
                    var rawValue = csv.GetField<string>(idx);
                    var value = string.IsNullOrEmpty(rawValue) ? null : rawValue;
                    nonPkValues[colName] = value;

                    hashBuilder.Append(colName).Append('=');
                    hashBuilder.Append(value ?? "NULL").Append('|');
                }
            }
            else
            {
                foreach (var idx in nonPrimaryKeyIndices)
                {
                    var colName = headers[idx];
                    var rawValue = csv.GetField<string>(idx);
                    var value = string.IsNullOrEmpty(rawValue) ? null : rawValue;
                    nonPkValues[colName] = value;

                    hashBuilder.Append(colName).Append('=');
                    hashBuilder.Append(value ?? "NULL").Append('|');
                }
            }

            // ハッシュ値を計算
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

    private class RowData
    {
        public Dictionary<string, object?> Values { get; set; } = new();
        public long Hash { get; set; }
    }
}
