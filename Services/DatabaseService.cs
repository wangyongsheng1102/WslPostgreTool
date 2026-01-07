using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Npgsql;
using WslPostgreTool.Models;

namespace WslPostgreTool.Services;

/// <summary>
/// データベース操作サービス
/// </summary>
public class DatabaseService
{
    /// <summary>
    /// テーブルリストを取得
    /// </summary>
    public async Task<List<TableInfo>> GetTablesAsync(string connectionString)
    {
        var tables = new List<TableInfo>();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // 第一步：获取所有表的基本信息
        const string sql = @"
        SELECT 
            table_schema,
            table_name,
            (SELECT COUNT(*) FROM information_schema.columns 
             WHERE table_schema = t.table_schema AND table_name = t.table_name) as column_count
        FROM information_schema.tables t
        WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
          AND table_type = 'BASE TABLE'
        ORDER BY table_schema, table_name";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        // 先读取所有表到临时列表
        var tableList = new List<(string Schema, string Table)>();
    
        while (await reader.ReadAsync())
        {
            tableList.Add((reader.GetString(0), reader.GetString(1)));
        }
    
        // 必须关闭第一个读取器
        await reader.CloseAsync();

        // 第二步：为每个表获取行数
        foreach (var (schemaName, tableName) in tableList)
        {
            try
            {
                var countSql = $"SELECT COUNT(*) FROM \"{schemaName}\".\"{tableName}\"";
                await using var countCmd = new NpgsqlCommand(countSql, conn);
                var rowCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync());

                tables.Add(new TableInfo
                {
                    SchemaName = schemaName,
                    TableName = tableName,
                    RowCount = rowCount
                });
            }
            catch (Exception ex)
            {
                // 如果无法获取行数，添加一个默认值
                tables.Add(new TableInfo
                {
                    SchemaName = schemaName,
                    TableName = tableName,
                    RowCount = -1,
                    // Error = ex.Message
                });
            }
        }

        return tables;
    }

    /// <summary>
    /// テーブルの主キー列を取得
    /// </summary>
    public async Task<List<string>> GetPrimaryKeyColumnsAsync(string connectionString, string schemaName, string tableName)
    {
        var columns = new List<string>();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        const string sql = @"
            SELECT column_name
            FROM information_schema.key_column_usage
            WHERE table_schema = @schema
              AND table_name = @table
              AND constraint_name IN (
                  SELECT constraint_name
                  FROM information_schema.table_constraints
                  WHERE constraint_type = 'PRIMARY KEY'
                    AND table_schema = @schema
                    AND table_name = @table
              )
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

    /// <summary>
    /// CSV エクスポート（COPY コマンド使用）
    /// </summary>
    public async Task ExportTableToCsvAsync(string connectionString, string schemaName, string tableName, string csvPath, IProgress<string>? progress = null)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        progress?.Report($"[処理中] テーブル '{schemaName}.{tableName}' をエクスポートしています...");

        // 全列を取得
        var allColumns = await GetAllColumnsAsync(conn, schemaName, tableName);
        
        // SELECT クエリでデータを取得
        var selectSql = $"SELECT {string.Join(", ", allColumns.Select(c => $"\"{c}\""))} FROM {schemaName}.{tableName}";
        await using var cmd = new NpgsqlCommand(selectSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        await using var fileWriter = new StreamWriter(csvPath, false, Encoding.UTF8);

        // ヘッダー行を書き込み
        await fileWriter.WriteLineAsync(string.Join(",", allColumns.Select(c => EscapeCsvValue(c))));

        // データ行を書き込み
        while (await reader.ReadAsync())
        {
            var rowValues = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.IsDBNull(i) ? "" : reader.GetValue(i)?.ToString() ?? "";
                rowValues.Add(EscapeCsvValue(value));
            }
            await fileWriter.WriteLineAsync(string.Join(",", rowValues));
        }

        await fileWriter.FlushAsync();
        progress?.Report($"[完了] テーブル '{schemaName}.{tableName}' のエクスポートが完了しました。");
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

    /// <summary>
    /// CSV インポート（TRUNCATE + COPY）
    /// </summary>
    public async Task ImportTableFromCsvAsync(string connectionString, string schemaName, string tableName, string csvPath, IProgress<string>? progress = null)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        progress?.Report($"[処理中] テーブル '{schemaName}.{tableName}' をクリアしています...");

        // TRUNCATE
        var truncateSql = $"TRUNCATE TABLE {schemaName}.{tableName}";
        await using var truncateCmd = new NpgsqlCommand(truncateSql, conn);
        await truncateCmd.ExecuteNonQueryAsync();

        progress?.Report($"[処理中] テーブル '{schemaName}.{tableName}' にデータをインポートしています...");

        // COPY FROM
        var copySql = $"COPY {schemaName}.{tableName} FROM STDIN WITH (FORMAT CSV, HEADER true)";
        await using var writer = conn.BeginBinaryImport(copySql);

        // await using var reader = new StreamReader(csvPath, Encoding.UTF8);
        using var reader = new StreamReader(csvPath, Encoding.UTF8);
        string? headerLine = await reader.ReadLineAsync(); // ヘッダー行をスキップ

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var values = ParseCsvLine(line);
            writer.StartRow();
            foreach (var value in values)
            {
                if (string.IsNullOrEmpty(value))
                    writer.WriteNull();
                else
                    writer.Write(value);
            }
        }

        await writer.CompleteAsync();
        progress?.Report($"[完了] テーブル '{schemaName}.{tableName}' のインポートが完了しました。");
    }

    private static string EscapeCsvValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                if (inQuotes && current.Length > 0 && current[current.Length - 1] == '"')
                {
                    // エスケープされた引用符
                    current.Length--;
                    current.Append('"');
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        values.Add(current.ToString());

        return values;
    }
}

