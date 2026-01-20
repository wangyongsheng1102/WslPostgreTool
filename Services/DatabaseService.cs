using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
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
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString
        };

        string database = builder.ContainsKey("username") ? builder["username"].ToString() : null;
        
        string schema = "public";
        if (database.StartsWith("cis"))
        {
            schema = "unisys";
        }
        
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
          AND table_schema = @schema
        ORDER BY table_schema, table_name";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("schema", schema);
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
    /// テーブルのコメント（日文名）を取得
    /// </summary>
    public async Task<string> GetTableCommentAsync(string connectionString, string schemaName, string tableName)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        const string sql = @"
            SELECT obj_description(c.oid, 'pg_class') as comment
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema
              AND c.relname = @table";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("schema", schemaName);
        cmd.Parameters.AddWithValue("table", tableName);

        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString() ?? tableName;
    }

    /// <summary>
    /// 列のコメント（日文名）を取得
    /// </summary>
    public async Task<Dictionary<string, string>> GetColumnCommentsAsync(string connectionString, string schemaName, string tableName)
    {
        var comments = new Dictionary<string, string>();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        const string sql = @"
        SELECT 
            c.column_name,
            COALESCE(pgd.description, '') as comment
        FROM information_schema.columns c
        JOIN pg_class pc ON pc.relname = c.table_name
        JOIN pg_namespace pn ON pn.oid = pc.relnamespace AND pn.nspname = c.table_schema
        LEFT JOIN pg_description pgd ON pgd.objoid = pc.oid 
            AND pgd.objsubid = c.ordinal_position
        WHERE c.table_schema = @schema
            AND c.table_name = @table
        ORDER BY c.ordinal_position";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("schema", schemaName);
        cmd.Parameters.AddWithValue("table", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var columnName = reader.GetString(0);
            var comment = reader.IsDBNull(1) ? columnName : reader.GetString(1);
            comments[columnName] = string.IsNullOrEmpty(comment) ? columnName : comment;
        }

        return comments;
    }

    /// <summary>
    /// CSV エクスポート（COPY コマンド使用）- Pythonと同じ動作を保証
    /// </summary>
    public async Task ExportTableToCsvAsync(string connectionString, string schemaName, string tableName, string csvPath, IProgress<string>? progress = null)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        progress?.Report($"[処理中] テーブル '{schemaName}.{tableName}' をエクスポートしています...");

        try
        {
            // Pythonの psycopg2 と同じパラメータ設定
            var copyCommand = $"COPY {schemaName}.{tableName} " +
                              $"TO STDOUT WITH (" +
                              $"FORMAT CSV, " +
                              $"HEADER true, " +
                              $"QUOTE '\"', " +           // クォート文字をダブルクォート
                              $"FORCE_QUOTE *, " +        // すべての列を強制的にクォート
                              $"ESCAPE '\"', " +          // エスケープ文字もダブルクォート
                              $"ENCODING 'UTF8'" +
                              $")";
        
            // テキストモードで出力（Pythonの TextIO と同じ）
            await using var fileStream = File.Create(csvPath);
            await using var streamWriter = new StreamWriter(fileStream, Encoding.UTF8);
        
            await using (var textWriter = conn.BeginTextExport(copyCommand))
            {
                string? line;
                while ((line = await textWriter.ReadLineAsync()) != null)
                {
                    await streamWriter.WriteLineAsync(line);
                }
            }

            progress?.Report($"[完了] テーブル '{schemaName}.{tableName}' のエクスポートが完了しました。");
        }
        catch (Exception ex)
        {
            progress?.Report($"[エラー] エクスポート中にエラーが発生しました: {ex.Message}");
            throw;
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
    
    // public async Task ImportTableFromCsvAsync(string connectionString, string schemaName, string tableName, string csvPath, IProgress<string>? progress = null)
    // {
    //     await using var conn = new NpgsqlConnection(connectionString);
    //     await conn.OpenAsync();
    //
    //     progress?.Report($"[処理中] テーブル '{schemaName}.{tableName}' をクリアしています...");
    //
    //     // TRUNCATE
    //     var truncateSql = $"TRUNCATE TABLE {schemaName}.{tableName}";
    //     await using var truncateCmd = new NpgsqlCommand(truncateSql, conn);
    //     await truncateCmd.ExecuteNonQueryAsync();
    //
    //     progress?.Report($"[処理中] テーブル '{schemaName}.{tableName}' にデータをインポートしています...");
    //
    //     // 最も簡単な方法：ファイルを直接コピー
    //     var copySql = $"COPY {schemaName}.{tableName} FROM @filePath WITH (FORMAT CSV, HEADER true)";
    //
    //     await using var copyCmd = new NpgsqlCommand(copySql, conn);
    //     copyCmd.Parameters.AddWithValue("filePath", csvPath);
    //
    //     await copyCmd.ExecuteNonQueryAsync();
    //
    //     progress?.Report($"[完了] テーブル '{schemaName}.{tableName}' のインポートが完了しました。");
    // }
    
    public async Task ImportTableFromCsvAsync(string connectionString, string schemaName, string tableName, string csvPath, IProgress<string>? progress = null)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            // テーブル存在チェック
            progress?.Report($"[処理中] テーブル '{schemaName}.{tableName}' の存在を確認しています...");
            
            var tableExists = await CheckTableExistsAsync(conn, schemaName, tableName);
            if (!tableExists)
            {
                progress?.Report($"[スキップ] テーブル '{schemaName}.{tableName}' は存在しません。インポートをスキップします。");
                return;
            }

            // ファイル存在チェック
            if (!File.Exists(csvPath))
            {
                progress?.Report($"[エラー] CSVファイル '{csvPath}' が存在しません。");
                throw new FileNotFoundException($"CSVファイル '{csvPath}' が見つかりません。");
            }
            
            // TRUNCATE（オプション）
            progress?.Report($"[処理中] テーブル '{schemaName}.{tableName}' をクリアしています...");
                
            try
            {
                await using var truncateCmd = new NpgsqlCommand($"TRUNCATE TABLE {schemaName}.{tableName}", conn);
                await truncateCmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                progress?.Report($"[警告] テーブルクリアに失敗しましたが続行します: {ex.Message}");
            }

            progress?.Report($"[処理中] テーブル '{schemaName}.{tableName}' にデータをインポートしています...");

            // COPY コマンドでインポート
            var copySql = $"COPY {schemaName}.{tableName} FROM STDIN WITH (" +
                         $"FORMAT CSV, " +
                         $"HEADER true, " +
                         $"DELIMITER ',', " +
                         $"QUOTE '\"', " +
                         $"ESCAPE '\"', " +
                         $"ENCODING 'UTF8'" +
                         $")";
            
            long importedRows = 0;
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                // 方法1: テキストインポートを使用（シンプルで確実）
                await using (var writer = conn.BeginTextImport(copySql))
                {
                    using var fileStream = File.OpenRead(csvPath);
                    using var reader = new StreamReader(fileStream, Encoding.UTF8);
                    
                    // ファイル全体をストリーミングでコピー
                    char[] buffer = new char[8192];
                    int charsRead;
                    
                    while ((charsRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await writer.WriteAsync(buffer, 0, charsRead);
                        
                        // 進捗報告（おおよそ）
                        importedRows += buffer.Count(c => c == '\n');
                        if (importedRows % 10000 == 0)
                        {
                            progress?.Report($"[処理中] 約 {importedRows:N0} 行を処理しました...");
                        }
                    }
                }
                
                stopwatch.Stop();
                progress?.Report($"[完了] テーブル '{schemaName}.{tableName}' のインポートが完了しました。");
                progress?.Report($"[情報] 処理時間: {stopwatch.Elapsed.TotalSeconds:F2} 秒");
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01") // テーブルが存在しないエラー
            {
                progress?.Report($"[警告] インポート中にテーブル '{schemaName}.{tableName}' が削除されました。");
                throw;
            }
        }
        catch (Exception ex)
        {
            progress?.Report($"[エラー] テーブル '{schemaName}.{tableName}' のインポート中にエラーが発生しました: {ex.Message}");
            throw;
        }
    }


    // テーブル存在チェック用のヘルパーメソッド
    private async Task<bool> CheckTableExistsAsync(NpgsqlConnection conn, string schemaName, string tableName)
    {
        var checkSql = @"
        SELECT EXISTS (
            SELECT 1 
            FROM information_schema.tables 
            WHERE table_schema = @schemaName 
            AND table_name = @tableName
        )";
    
        await using var checkCmd = new NpgsqlCommand(checkSql, conn);
        checkCmd.Parameters.AddWithValue("@schemaName", schemaName);
        checkCmd.Parameters.AddWithValue("@tableName", tableName);
    
        var result = await checkCmd.ExecuteScalarAsync();
        return result != null && result != DBNull.Value && Convert.ToBoolean(result);
    }

    // オプション：より詳細なチェックを行うメソッド（カラム構造の確認など）
    private async Task<bool> ValidateTableStructureAsync(NpgsqlConnection conn, string schemaName, string tableName, string csvPath)
    {
        // CSVのヘッダーを読み取る
        using var reader = new StreamReader(csvPath, Encoding.UTF8);
        var headerLine = await reader.ReadLineAsync();
        if (string.IsNullOrEmpty(headerLine))
        {
            return false;
        }
        
        var csvHeaders = headerLine.Split(',').Select(h => h.Trim()).ToList();
        
        // テーブルのカラム情報を取得
        var columnSql = @"
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = @schemaName 
            AND table_name = @tableName
            ORDER BY ordinal_position";
        
        await using var cmd = new NpgsqlCommand(columnSql, conn);
        cmd.Parameters.AddWithValue("@schemaName", schemaName);
        cmd.Parameters.AddWithValue("@tableName", tableName);
        
        await using var reader2 = await cmd.ExecuteReaderAsync();
        var tableColumns = new List<string>();
        
        while (await reader2.ReadAsync())
        {
            tableColumns.Add(reader2.GetString(0));
        }
        
        // カラム数の比較（オプション）
        return csvHeaders.Count == tableColumns.Count;
    }

    // /// <summary>
    // /// CSV インポート（TRUNCATE + COPY）
    // /// </summary>
    // public async Task ImportTableFromCsvAsync(string connectionString, string schemaName, string tableName, string csvPath, IProgress<string>? progress = null)
    // {
    //     await using var conn = new NpgsqlConnection(connectionString);
    //     await conn.OpenAsync();
    //
    //     progress?.Report($"[処理中] テーブル '{schemaName}.{tableName}' をクリアしています...");
    //
    //     // TRUNCATE
    //     var truncateSql = $"TRUNCATE TABLE {schemaName}.{tableName}";
    //     await using var truncateCmd = new NpgsqlCommand(truncateSql, conn);
    //     await truncateCmd.ExecuteNonQueryAsync();
    //
    //     progress?.Report($"[処理中] テーブル '{schemaName}.{tableName}' にデータをインポートしています...");
    //
    //     // COPY FROM
    //     // var copySql = $"COPY {schemaName}.{tableName} FROM STDIN WITH (FORMAT CSV, HEADER true)";
    //     var copySql = $"COPY {schemaName}.{tableName} FROM STDIN WITH (FORMAT CSV, HEADER true, DELIMITER ',', QUOTE '\"')";
    //     await using var writer = conn.BeginBinaryImport(copySql);
    //
    //     // await using var reader = new StreamReader(csvPath, Encoding.UTF8);
    //     using var reader = new StreamReader(csvPath, Encoding.UTF8);
    //     string? headerLine = await reader.ReadLineAsync(); // ヘッダー行をスキップ
    //
    //     string? line;
    //     while ((line = await reader.ReadLineAsync()) != null)
    //     {
    //         if (string.IsNullOrWhiteSpace(line)) continue;
    //
    //         var values = ParseCsvLine(line);
    //         writer.StartRow();
    //         foreach (var value in values)
    //         {
    //             if (string.IsNullOrEmpty(value))
    //                 writer.WriteNull();
    //             else
    //                 writer.Write(value);
    //         }
    //     }
    //
    //     await writer.CompleteAsync();
    //     progress?.Report($"[完了] テーブル '{schemaName}.{tableName}' のインポートが完了しました。");
    // }

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

