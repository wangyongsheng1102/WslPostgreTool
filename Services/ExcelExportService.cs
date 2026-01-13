using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using WslPostgreTool.Models;

namespace WslPostgreTool.Services;

/// <summary>
/// Excel エクスポートサービス
/// </summary>
public class ExcelExportService
{
    private readonly DatabaseService _databaseService = new();

    /// <summary>
    /// 比較結果を Excel にエクスポート（1つのシートに統合）
    /// 前 = Base数据
    /// 现行的后 = Old数据
    /// 新的后 = New数据
    /// </summary>
    public void ExportComparisonResults(
        string filePath, 
        List<RowComparisonResult> baseVsOldResults,
        List<RowComparisonResult> baseVsNewResults,
        string connectionString)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("データ比較");

        // A1: テストデータ参照用（红色字体）
        worksheet.Cell(1, 1).Value = "[自動採番]、[登録/更新/削除日時]、[登録/更新/削除者]、[登録/更新/削除機能]のデータ比較結果が「FALSE」の場合、補足説明が必要がない。";
        worksheet.Cell(1, 1).Style.Font.FontColor = XLColor.Red;
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = 11;
        worksheet.Cell(1, 1).Style.Font.SetFontName("MS PGothic");
        worksheet.Column("A").Width = 3.5;

        int currentRow = 3;

        // テーブルごとにグループ化
        var allTableNames = baseVsOldResults.Select(r => r.TableName)
            .Union(baseVsNewResults.Select(r => r.TableName))
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        foreach (var tableName in allTableNames)
        {
            var oldResults = baseVsOldResults.Where(r => r.TableName == tableName).ToList();
            var newResults = baseVsNewResults.Where(r => r.TableName == tableName).ToList();

            // 解析表名
            var parts = tableName.Split('.');
            string schemaName = parts.Length >= 2 ? parts[0] : "public";
            string nameOnly = parts.Length >= 2 ? string.Join(".", parts.Skip(1)) : tableName;

            // 获取表注释和列注释
            string tableComment = nameOnly;
            Dictionary<string, string> columnComments = new();
            
            try
            {
                tableComment = _databaseService.GetTableCommentAsync(connectionString, schemaName, nameOnly).Result;
                columnComments = _databaseService.GetColumnCommentsAsync(connectionString, schemaName, nameOnly).Result;
            }
            catch
            {
                tableComment = nameOnly;
            }

            // 收集所有列名
            var allColumns = new HashSet<string>();
            foreach (var result in oldResults.Concat(newResults))
            {
                foreach (var key in result.PrimaryKeyValues.Keys)
                    allColumns.Add(key);
                foreach (var key in result.OldValues.Keys)
                    allColumns.Add(key);
                foreach (var key in result.NewValues.Keys)
                    allColumns.Add(key);
            }

            var columns = allColumns.OrderBy(c => c).ToList();
            int headerStartCol = 3;

            // ========== 現行システム ==========
            worksheet.Cell(currentRow, 2).Value = "現行システム";
            worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 2).Style.Font.FontSize = 12;
            currentRow++;

            // 現行システム：增删改前 - 表名和列名
            worksheet.Cell(currentRow, headerStartCol).Value = tableComment;
            worksheet.Cell(currentRow, headerStartCol).Style.Font.Bold = true;
            currentRow++;
            worksheet.Cell(currentRow, headerStartCol).Value = tableName;
            worksheet.Cell(currentRow, headerStartCol).Style.Font.Bold = true;
            currentRow++;

            // 列头：列日文名（第一行）
            int colHeaderRow1 = currentRow;
            worksheet.Cell(colHeaderRow1, 1).Value = "";
            worksheet.Cell(colHeaderRow1, 2).Value = "";
            
            int colIndex = headerStartCol;
            foreach (var column in columns)
            {
                var columnComment = columnComments.TryGetValue(column, out var comment) ? comment : column;
                worksheet.Cell(colHeaderRow1, colIndex).Value = columnComment;
                worksheet.Cell(colHeaderRow1, colIndex).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
                worksheet.Cell(colHeaderRow1, colIndex).Style.Font.Bold = true;
                ApplyCellBorder(worksheet.Cell(colHeaderRow1, colIndex));
                colIndex++;
            }
            currentRow++;

            // 列头：列英文名（第二行）
            colIndex = headerStartCol;
            foreach (var column in columns)
            {
                worksheet.Cell(currentRow, colIndex).Value = column;
                worksheet.Cell(currentRow, colIndex).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
                worksheet.Cell(currentRow, colIndex).Style.Font.Bold = true;
                ApplyCellBorder(worksheet.Cell(currentRow, colIndex));
                colIndex++;
            }
            currentRow++;

            // 存储现行系统的"前"和"后"数据行位置（用于后续比较）
            var oldBeforeRowMap = new Dictionary<string, int>(); // 主键 -> "前"数据行
            var oldAfterRowMap = new Dictionary<string, int>(); // 主键 -> "后"数据行

            // 現行システム：增删改前 - 前数据
            foreach (var result in oldResults)
            {
                string pkKey = string.Join("|", result.PrimaryKeyValues.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));
                
                string statusLabel = GetBeforeLabel(result.Status);
                worksheet.Cell(currentRow, 2).Value = statusLabel;
                worksheet.Cell(currentRow, 2).Style.Font.Bold = true;

                // 存储"前"数据行的位置
                oldBeforeRowMap[pkKey] = currentRow;

                int dataCol = headerStartCol;
                foreach (var column in columns)
                {
                    // Base数据：从OldValues获取
                    // 新增场合：前应该是空行
                    object? value = result.Status == ComparisonStatus.Added ? null : GetBaseValue(result, column);
                    worksheet.Cell(currentRow, dataCol).Value = value?.ToString() ?? "";
                    ApplyCellBorder(worksheet.Cell(currentRow, dataCol));
                    dataCol++;
                }

                currentRow++;
            }

            currentRow++; // 空行

            // 現行システム：增删改后 - 表名和列名
            worksheet.Cell(currentRow, headerStartCol).Value = tableComment;
            worksheet.Cell(currentRow, headerStartCol).Style.Font.Bold = true;
            currentRow++;
            worksheet.Cell(currentRow, headerStartCol).Value = tableName;
            worksheet.Cell(currentRow, headerStartCol).Style.Font.Bold = true;
            currentRow++;

            // 列头：列日文名（第一行）
            colHeaderRow1 = currentRow;
            worksheet.Cell(colHeaderRow1, 1).Value = "";
            worksheet.Cell(colHeaderRow1, 2).Value = "";
            
            colIndex = headerStartCol;
            foreach (var column in columns)
            {
                var columnComment = columnComments.TryGetValue(column, out var comment) ? comment : column;
                worksheet.Cell(colHeaderRow1, colIndex).Value = columnComment;
                worksheet.Cell(colHeaderRow1, colIndex).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
                worksheet.Cell(colHeaderRow1, colIndex).Style.Font.Bold = true;
                ApplyCellBorder(worksheet.Cell(colHeaderRow1, colIndex));
                colIndex++;
            }
            currentRow++;

            // 列头：列英文名（第二行）
            colIndex = headerStartCol;
            foreach (var column in columns)
            {
                worksheet.Cell(currentRow, colIndex).Value = column;
                worksheet.Cell(currentRow, colIndex).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
                worksheet.Cell(currentRow, colIndex).Style.Font.Bold = true;
                ApplyCellBorder(worksheet.Cell(currentRow, colIndex));
                colIndex++;
            }
            currentRow++;

            // 現行システム：增删改后 - 后数据
            foreach (var result in oldResults)
            {
                string pkKey = string.Join("|", result.PrimaryKeyValues.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));
                
                string statusLabel = GetAfterLabel(result.Status);
                worksheet.Cell(currentRow, 2).Value = statusLabel;
                worksheet.Cell(currentRow, 2).Style.Font.Bold = true;

                // 存储"后"数据行的位置
                oldAfterRowMap[pkKey] = currentRow;

                int dataCol = headerStartCol;
                foreach (var column in columns)
                {
                    // Old数据：从NewValues获取（删除场合NewValues为空，会自然返回null）
                    object? value = GetOldValue(result, column);
                    worksheet.Cell(currentRow, dataCol).Value = value?.ToString() ?? "";
                    ApplyCellBorder(worksheet.Cell(currentRow, dataCol));
                    dataCol++;
                }

                currentRow++;
            }

            currentRow += 2; // 系统之间的空行

            // ========== 新システム ==========
            worksheet.Cell(currentRow, 2).Value = "新システム";
            worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 2).Style.Font.FontSize = 12;
            currentRow++;

            // 新システム：增删改前 - 表名和列名
            worksheet.Cell(currentRow, headerStartCol).Value = tableComment;
            worksheet.Cell(currentRow, headerStartCol).Style.Font.Bold = true;
            currentRow++;
            worksheet.Cell(currentRow, headerStartCol).Value = tableName;
            worksheet.Cell(currentRow, headerStartCol).Style.Font.Bold = true;
            currentRow++;

            // 列头：列日文名（第一行）
            colHeaderRow1 = currentRow;
            worksheet.Cell(colHeaderRow1, 1).Value = "";
            worksheet.Cell(colHeaderRow1, 2).Value = "";
            
            colIndex = headerStartCol;
            foreach (var column in columns)
            {
                var columnComment = columnComments.TryGetValue(column, out var comment) ? comment : column;
                worksheet.Cell(colHeaderRow1, colIndex).Value = columnComment;
                worksheet.Cell(colHeaderRow1, colIndex).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
                worksheet.Cell(colHeaderRow1, colIndex).Style.Font.Bold = true;
                ApplyCellBorder(worksheet.Cell(colHeaderRow1, colIndex));
                colIndex++;
            }
            currentRow++;

            // 列头：列英文名（第二行）
            colIndex = headerStartCol;
            foreach (var column in columns)
            {
                worksheet.Cell(currentRow, colIndex).Value = column;
                worksheet.Cell(currentRow, colIndex).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
                worksheet.Cell(currentRow, colIndex).Style.Font.Bold = true;
                ApplyCellBorder(worksheet.Cell(currentRow, colIndex));
                colIndex++;
            }
            currentRow++;

            // 存储新系统的"前"和"后"数据行位置
            var newBeforeRowMap = new Dictionary<string, int>(); // 主键 -> "前"数据行
            var newAfterRowMap = new Dictionary<string, int>(); // 主键 -> "后"数据行

            // 新システム：增删改前 - 前数据
            foreach (var result in newResults)
            {
                string pkKey = string.Join("|", result.PrimaryKeyValues.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));
                
                string statusLabel = GetBeforeLabel(result.Status);
                worksheet.Cell(currentRow, 2).Value = statusLabel;
                worksheet.Cell(currentRow, 2).Style.Font.Bold = true;

                // 存储"前"数据行的位置
                newBeforeRowMap[pkKey] = currentRow;

                int dataCol = headerStartCol;
                foreach (var column in columns)
                {
                    // Base数据：从OldValues获取
                    // 新增场合：前应该是空行
                    object? value = result.Status == ComparisonStatus.Added ? null : GetBaseValue(result, column);
                    worksheet.Cell(currentRow, dataCol).Value = value?.ToString() ?? "";
                    ApplyCellBorder(worksheet.Cell(currentRow, dataCol));
                    dataCol++;
                }

                currentRow++;
            }

            currentRow++; // 空行

            // 新システム：增删改后 - 表名和列名
            worksheet.Cell(currentRow, headerStartCol).Value = tableComment;
            worksheet.Cell(currentRow, headerStartCol).Style.Font.Bold = true;
            currentRow++;
            worksheet.Cell(currentRow, headerStartCol).Value = tableName;
            worksheet.Cell(currentRow, headerStartCol).Style.Font.Bold = true;
            currentRow++;

            // 列头：列日文名（第一行）
            colHeaderRow1 = currentRow;
            worksheet.Cell(colHeaderRow1, 1).Value = "";
            worksheet.Cell(colHeaderRow1, 2).Value = "";
            
            colIndex = headerStartCol;
            foreach (var column in columns)
            {
                var columnComment = columnComments.TryGetValue(column, out var comment) ? comment : column;
                worksheet.Cell(colHeaderRow1, colIndex).Value = columnComment;
                worksheet.Cell(colHeaderRow1, colIndex).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
                worksheet.Cell(colHeaderRow1, colIndex).Style.Font.Bold = true;
                ApplyCellBorder(worksheet.Cell(colHeaderRow1, colIndex));
                colIndex++;
            }
            currentRow++;

            // 列头：列英文名（第二行）
            colIndex = headerStartCol;
            foreach (var column in columns)
            {
                worksheet.Cell(currentRow, colIndex).Value = column;
                worksheet.Cell(currentRow, colIndex).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
                worksheet.Cell(currentRow, colIndex).Style.Font.Bold = true;
                ApplyCellBorder(worksheet.Cell(currentRow, colIndex));
                colIndex++;
            }
            currentRow++;

            // 新システム：增删改后 - 后数据
            foreach (var result in newResults)
            {
                string pkKey = string.Join("|", result.PrimaryKeyValues.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));
                
                string statusLabel = GetAfterLabel(result.Status);
                worksheet.Cell(currentRow, 2).Value = statusLabel;
                worksheet.Cell(currentRow, 2).Style.Font.Bold = true;

                // 存储"后"数据行的位置
                newAfterRowMap[pkKey] = currentRow;

                int dataCol = headerStartCol;
                foreach (var column in columns)
                {
                    // New数据：从NewValues获取（删除场合NewValues为空，会自然返回null）
                    object? value = GetNewValue(result, column);
                    worksheet.Cell(currentRow, dataCol).Value = value?.ToString() ?? "";
                    ApplyCellBorder(worksheet.Cell(currentRow, dataCol));
                    dataCol++;
                }

                currentRow++;
            }

            currentRow += 2;

            // ========== 比較結果 ==========
            // 只比较新旧的后数据，不需要表名，只需要列名
            worksheet.Cell(currentRow, 2).Value = "比較結果";
            worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 2).Style.Font.FontSize = 12;
            currentRow++;

            // 比较结果列头（只需要列名，不需要表名）
            worksheet.Cell(currentRow, 2).Value = "";
            colIndex = headerStartCol;
            foreach (var column in columns)
            {
                var columnComment = columnComments.TryGetValue(column, out var comment) ? comment : column;
                worksheet.Cell(currentRow, colIndex).Value = columnComment;
                worksheet.Cell(currentRow, colIndex).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
                worksheet.Cell(currentRow, colIndex).Style.Font.Bold = true;
                ApplyCellBorder(worksheet.Cell(currentRow, colIndex));
                colIndex++;
            }
            currentRow++;

            // 创建主键到Result的映射，方便查找
            var oldResultMap = oldResults.ToDictionary(r => 
                string.Join("|", r.PrimaryKeyValues.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
            var newResultMap = newResults.ToDictionary(r => 
                string.Join("|", r.PrimaryKeyValues.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));

            // 收集所有主键（确保新旧数据有几条，就比较几个）
            var allPkKeys = oldAfterRowMap.Keys.Union(newAfterRowMap.Keys).Distinct().ToList();

            // 创建一个专门用于空值比较的单元格（在比较结果区域之前）
            int emptyCellRow = currentRow - 1; // 使用比较结果列头行的上一行
            int emptyCellCol = headerStartCol;
            worksheet.Cell(emptyCellRow, emptyCellCol).Value = ""; // 确保这个单元格是空的
            string emptyCellRef = GetCellReference(emptyCellRow, emptyCellCol);

            // 对每个主键进行比较（比较的都是"后"数据）
            foreach (var pkKey in allPkKeys)
            {
                bool hasOld = oldAfterRowMap.TryGetValue(pkKey, out int oldRow);
                bool hasNew = newAfterRowMap.TryGetValue(pkKey, out int newRow);

                // 比较标签：强调现行/新系统各自的"后"状态
                RowComparisonResult? oldResult = null;
                RowComparisonResult? newResult = null;
                ComparisonStatus? oldStatus = null;
                ComparisonStatus? newStatus = null;

                if (hasOld && oldResultMap.TryGetValue(pkKey, out oldResult))
                {
                    oldStatus = oldResult.Status;
                }
                if (hasNew && newResultMap.TryGetValue(pkKey, out newResult))
                {
                    newStatus = newResult.Status;
                }

                worksheet.Cell(currentRow, 2).Value = BuildCompareLabel(oldStatus, newStatus);
                worksheet.Cell(currentRow, 2).Style.Font.Bold = true;

                colIndex = headerStartCol;
                foreach (var column in columns)
                {
                    // 比较两个"后"单元格（Old的"后"和New的"后"）
                    // 始终使用单元格引用，即使单元格是空的
                    string oldCellRef = hasOld ? GetCellReference(oldRow, colIndex) : emptyCellRef;
                    string newCellRef = hasNew ? GetCellReference(newRow, colIndex) : emptyCellRef;
                    string formula = $"=EXACT({oldCellRef},{newCellRef})";
                    
                    var cell = worksheet.Cell(currentRow, colIndex);
                    cell.SetFormulaA1(formula);
                    ApplyCellBorder(cell);

                    // 设置条件格式：FALSE时黄色背景
                    var conditionalFormat = cell.AddConditionalFormat();
                    var currentCellRef = GetCellReference(currentRow, colIndex);
                    conditionalFormat.WhenIsTrue($"={currentCellRef}=FALSE").Fill.SetBackgroundColor(XLColor.Yellow);
                    
                    colIndex++;
                }

                currentRow++;
            }

            currentRow += 2; // 表之间的空行
        }

        worksheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);
    }

    /// <summary>
    /// 获取Base数据（前）
    /// </summary>
    private static object? GetBaseValue(RowComparisonResult result, string column)
    {
        // 优先从主键获取
        if (result.PrimaryKeyValues.TryGetValue(column, out var pkValue))
        {
            return pkValue;
        }

        // Base数据从OldValues获取
        if (result.OldValues.TryGetValue(column, out var oldValue))
        {
            return oldValue;
        }

        return null;
    }

    /// <summary>
    /// 获取Old数据（现行的后）
    /// </summary>
    private static object? GetOldValue(RowComparisonResult result, string column)
    {
        // 优先从主键获取
        if (result.PrimaryKeyValues.TryGetValue(column, out var pkValue))
        {
            return pkValue;
        }

        // Old数据从NewValues获取（因为baseVsOldResults中NewValues是Old数据）
        if (result.NewValues.TryGetValue(column, out var newValue))
        {
            return newValue;
        }

        return null;
    }

    /// <summary>
    /// 获取New数据（新的后）
    /// </summary>
    private static object? GetNewValue(RowComparisonResult result, string column)
    {
        // 优先从主键获取
        if (result.PrimaryKeyValues.TryGetValue(column, out var pkValue))
        {
            return pkValue;
        }

        // New数据从NewValues获取（因为baseVsNewResults中NewValues是New数据）
        if (result.NewValues.TryGetValue(column, out var newValue))
        {
            return newValue;
        }

        return null;
    }

    private static string GetBeforeLabel(ComparisonStatus status) =>
        status switch
        {
            ComparisonStatus.Deleted => "削除前",
            ComparisonStatus.Added => "追加前",
            ComparisonStatus.Updated => "更新前",
            _ => "更新前"
        };

    private static string GetAfterLabel(ComparisonStatus status) =>
        status switch
        {
            ComparisonStatus.Deleted => "削除後",
            ComparisonStatus.Added => "追加後",
            ComparisonStatus.Updated => "更新後",
            _ => "更新後"
        };

    private static string BuildCompareLabel(ComparisonStatus? oldStatus, ComparisonStatus? newStatus)
    {
        string oldText = oldStatus.HasValue ? $"現行{GetAfterLabel(oldStatus.Value)}" : "現行(該当なし)";
        string newText = newStatus.HasValue ? $"新{GetAfterLabel(newStatus.Value)}" : "新(該当なし)";
        return $"{oldText} / {newText}";
    }

    private static void ApplyCellBorder(IXLCell cell)
    {
        cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.RightBorder = XLBorderStyleValues.Thin;
    }

    private static string GetCellReference(int row, int column)
    {
        // 将列号转换为Excel列名（1=A, 2=B, 3=C, ...）
        string columnName = "";
        int col = column;
        while (col > 0)
        {
            col--;
            columnName = (char)('A' + (col % 26)) + columnName;
            col /= 26;
        }
        return $"{columnName}{row}";
    }
}
