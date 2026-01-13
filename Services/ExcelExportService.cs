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

            // 表头
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

            // 現行システム
            // 前 = Base数据 (OldValues)
            // 后 = Old数据 (NewValues)
            worksheet.Cell(currentRow, 2).Value = "現行システム";
            worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 2).Style.Font.FontSize = 12;
            currentRow++;

            // 存储现行系统的"后"数据行位置（用于后续比较）
            var oldDataRowMap = new Dictionary<string, int>(); // 主键 -> "后"数据行

            // 现行系统的数据
            foreach (var result in oldResults)
            {
                string pkKey = string.Join("|", result.PrimaryKeyValues.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));
                
                // 根据Status确定标签
                string statusLabel = result.Status switch
                {
                    ComparisonStatus.Deleted => "削除前",
                    ComparisonStatus.Added => "追加前",
                    ComparisonStatus.Updated => "更新前",
                    _ => "更新前"
                };

                // 前 = Base数据 (OldValues)
                worksheet.Cell(currentRow, 2).Value = statusLabel;
                worksheet.Cell(currentRow, 2).Style.Font.Bold = true;

                int dataCol = headerStartCol;
                foreach (var column in columns)
                {
                    // Base数据：从OldValues获取
                    object? value = GetBaseValue(result, column);
                    worksheet.Cell(currentRow, dataCol).Value = value?.ToString() ?? "";
                    ApplyCellBorder(worksheet.Cell(currentRow, dataCol));
                    dataCol++;
                }

                currentRow++;

                // 后 = Old数据 (NewValues)
                string statusLabel2 = result.Status switch
                {
                    ComparisonStatus.Deleted => "削除後",
                    ComparisonStatus.Added => "追加後",
                    ComparisonStatus.Updated => "更新後",
                    _ => "更新後"
                };

                worksheet.Cell(currentRow, 2).Value = statusLabel2;
                worksheet.Cell(currentRow, 2).Style.Font.Bold = true;

                // 存储"后"数据行的位置
                oldDataRowMap[pkKey] = currentRow;

                dataCol = headerStartCol;
                foreach (var column in columns)
                {
                    // Old数据：从NewValues获取
                    object? value = GetOldValue(result, column);
                    worksheet.Cell(currentRow, dataCol).Value = value?.ToString() ?? "";
                    ApplyCellBorder(worksheet.Cell(currentRow, dataCol));
                    dataCol++;
                }

                currentRow++;
                currentRow++; // 空行
            }

            currentRow += 2; // 系统之间的空行

            // 新システム
            // 前 = Base数据 (OldValues)
            // 后 = New数据 (NewValues)
            worksheet.Cell(currentRow, 2).Value = "新システム";
            worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 2).Style.Font.FontSize = 12;
            currentRow++;

            // 存储新系统的"后"数据行位置
            var newDataRowMap = new Dictionary<string, int>();

            // 新系统的数据
            foreach (var result in newResults)
            {
                string pkKey = string.Join("|", result.PrimaryKeyValues.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));
                
                // 根据Status确定标签
                string statusLabel = result.Status switch
                {
                    ComparisonStatus.Deleted => "削除前",
                    ComparisonStatus.Added => "追加前",
                    ComparisonStatus.Updated => "更新前",
                    _ => "更新前"
                };

                // 前 = Base数据 (OldValues)
                worksheet.Cell(currentRow, 2).Value = statusLabel;
                worksheet.Cell(currentRow, 2).Style.Font.Bold = true;

                int dataCol = headerStartCol;
                foreach (var column in columns)
                {
                    // Base数据：从OldValues获取
                    object? value = GetBaseValue(result, column);
                    worksheet.Cell(currentRow, dataCol).Value = value?.ToString() ?? "";
                    ApplyCellBorder(worksheet.Cell(currentRow, dataCol));
                    dataCol++;
                }

                currentRow++;

                // 后 = New数据 (NewValues)
                string statusLabel2 = result.Status switch
                {
                    ComparisonStatus.Deleted => "削除後",
                    ComparisonStatus.Added => "追加後",
                    ComparisonStatus.Updated => "更新後",
                    _ => "更新後"
                };

                worksheet.Cell(currentRow, 2).Value = statusLabel2;
                worksheet.Cell(currentRow, 2).Style.Font.Bold = true;

                // 存储"后"数据行的位置
                newDataRowMap[pkKey] = currentRow;

                dataCol = headerStartCol;
                foreach (var column in columns)
                {
                    // New数据：从NewValues获取
                    object? value = GetNewValue(result, column);
                    worksheet.Cell(currentRow, dataCol).Value = value?.ToString() ?? "";
                    ApplyCellBorder(worksheet.Cell(currentRow, dataCol));
                    dataCol++;
                }

                currentRow++;
                currentRow++; // 空行
            }

            currentRow += 2;

            // 比較結果
            // 比较：现行的后(Old) vs 新的后(New)
            // 确保新旧数据有几条，就比较几个
            worksheet.Cell(currentRow, 2).Value = "比較結果";
            worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 2).Style.Font.FontSize = 12;
            currentRow++;

            // 比较结果列头
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
            var allPkKeys = oldDataRowMap.Keys.Union(newDataRowMap.Keys).Distinct().ToList();

            // 对每个主键进行比较（比较的都是"后"数据）
            foreach (var pkKey in allPkKeys)
            {
                bool hasOld = oldDataRowMap.TryGetValue(pkKey, out int oldRow);
                bool hasNew = newDataRowMap.TryGetValue(pkKey, out int newRow);

                // 确定状态标签（根据"后"的状态）
                string statusLabel = "";
                RowComparisonResult? oldResult = null;
                RowComparisonResult? newResult = null;

                if (hasOld && oldResultMap.TryGetValue(pkKey, out oldResult))
                {
                    // 根据oldResult的状态确定标签（因为比较的是"后"）
                    statusLabel = oldResult.Status switch
                    {
                        ComparisonStatus.Deleted => "削除後",
                        ComparisonStatus.Added => "追加後",
                        ComparisonStatus.Updated => "更新後",
                        _ => "更新後"
                    };
                }
                else if (hasNew && newResultMap.TryGetValue(pkKey, out newResult))
                {
                    // 根据newResult的状态确定标签（因为比较的是"后"）
                    statusLabel = newResult.Status switch
                    {
                        ComparisonStatus.Deleted => "削除後",
                        ComparisonStatus.Added => "追加後",
                        ComparisonStatus.Updated => "更新後",
                        _ => "更新後"
                    };
                }

                worksheet.Cell(currentRow, 2).Value = statusLabel;
                worksheet.Cell(currentRow, 2).Style.Font.Bold = true;

                colIndex = headerStartCol;
                foreach (var column in columns)
                {
                    // 比较两个"后"单元格（Old的"后"和New的"后"）
                    if (hasOld && hasNew)
                    {
                        // 两个"后"单元格都存在，使用EXACT比较
                        string oldCellRef = GetCellReference(oldRow, colIndex);
                        string newCellRef = GetCellReference(newRow, colIndex);
                        string formula = $"=EXACT({oldCellRef},{newCellRef})";
                        
                        var cell = worksheet.Cell(currentRow, colIndex);
                        // ClosedXML 0.105+ 使用 SetFormulaA1 设定公式
                        cell.SetFormulaA1(formula);
                        ApplyCellBorder(cell);

                        // 设置条件格式：FALSE时黄色背景
                        var conditionalFormat = cell.AddConditionalFormat();
                        var currentCellRef = GetCellReference(currentRow, colIndex);
                        conditionalFormat.WhenIsTrue($"={currentCellRef}=FALSE").Fill.SetBackgroundColor(XLColor.Yellow);
                    }
                    else if (hasOld)
                    {
                        // 只有Old的"后"存在，New的"后"不存在（可能是删除或新增）
                        // 比较Old的"后"和空值
                        string oldCellRef = GetCellReference(oldRow, colIndex);
                        string formula = $"=EXACT({oldCellRef},\"\")";
                        
                        var cell = worksheet.Cell(currentRow, colIndex);
                        cell.SetFormulaA1(formula);
                        ApplyCellBorder(cell);

                        // 设置条件格式：FALSE时黄色背景
                        var conditionalFormat = cell.AddConditionalFormat();
                        var currentCellRef = GetCellReference(currentRow, colIndex);
                        conditionalFormat.WhenIsTrue($"={currentCellRef}=FALSE").Fill.SetBackgroundColor(XLColor.Yellow);
                    }
                    else if (hasNew)
                    {
                        // 只有New的"后"存在，Old的"后"不存在（可能是删除或新增）
                        // 比较空值和New的"后"
                        string newCellRef = GetCellReference(newRow, colIndex);
                        string formula = $"=EXACT(\"\",{newCellRef})";
                        
                        var cell = worksheet.Cell(currentRow, colIndex);
                        cell.SetFormulaA1(formula);
                        ApplyCellBorder(cell);

                        // 设置条件格式：FALSE时黄色背景
                        var conditionalFormat = cell.AddConditionalFormat();
                        var currentCellRef = GetCellReference(currentRow, colIndex);
                        conditionalFormat.WhenIsTrue($"={currentCellRef}=FALSE").Fill.SetBackgroundColor(XLColor.Yellow);
                    }
                    else
                    {
                        // 理论上不应该到达这里，但为了安全起见
                        worksheet.Cell(currentRow, colIndex).Value = "FALSE";
                        worksheet.Cell(currentRow, colIndex).Style.Fill.BackgroundColor = XLColor.Yellow;
                        ApplyCellBorder(worksheet.Cell(currentRow, colIndex));
                    }
                    
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

        // 新增场合：Base数据（前）应该是空白的
        if (result.Status == ComparisonStatus.Added)
        {
            return null;
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

        // 删除场合：Old数据（后）应该是空白的
        if (result.Status == ComparisonStatus.Deleted)
        {
            return null;
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

        // 删除场合：New数据（后）应该是空白的
        if (result.Status == ComparisonStatus.Deleted)
        {
            return null;
        }

        return null;
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
