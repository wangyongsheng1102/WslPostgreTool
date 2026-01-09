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
    /// 比較結果を Excel にエクスポート（2つのシート：Base vs Old と Base vs New）
    /// </summary>
    public void ExportComparisonResults(
        string filePath, 
        List<RowComparisonResult> baseVsOldResults,
        List<RowComparisonResult> baseVsNewResults,
        string connectionString)
    {
        using var workbook = new XLWorkbook();
        
        // Base vs Old のシート
        CreateComparisonSheet(workbook, "更新前 vs 旧", baseVsOldResults, connectionString);
        
        // Base vs New のシート
        CreateComparisonSheet(workbook, "更新前 vs 新", baseVsNewResults, connectionString);

        workbook.SaveAs(filePath);
    }

    private void CreateComparisonSheet(
        XLWorkbook workbook, 
        string sheetName, 
        List<RowComparisonResult> results,
        string connectionString)
    {
        var worksheet = workbook.Worksheets.Add(sheetName);

        // A1: テストデータ参照用（红色字体）
        worksheet.Cell(1, 1).Value = "テストデータ参照用";
        worksheet.Cell(1, 1).Style.Font.FontColor = XLColor.Red;
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = 14;

        if (!results.Any())
        {
            worksheet.Columns().AdjustToContents();
            return;
        }

        // テーブルごとにグループ化
        var groupedByTable = results.GroupBy(r => r.TableName).ToList();

        int currentRow = 3; // 从第3行开始（A1是标题，第2行留空）

        foreach (var tableGroup in groupedByTable)
        {
            var tableName = tableGroup.Key;
            var tableResults = tableGroup.ToList();

            // 解析表名（schema.table格式）
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
                // 如果查询失败，使用表名作为注释
                tableComment = nameOnly;
            }

            // 收集所有列名（主键 + 旧值 + 新值）
            var allColumns = new HashSet<string>();
            foreach (var result in tableResults)
            {
                foreach (var key in result.PrimaryKeyValues.Keys)
                    allColumns.Add(key);
                foreach (var key in result.OldValues.Keys)
                    allColumns.Add(key);
                foreach (var key in result.NewValues.Keys)
                    allColumns.Add(key);
            }

            var columns = allColumns.OrderBy(c => c).ToList();

            // C列开始：表日文名、表名（绿色底色）
            int headerStartCol = 3;
            worksheet.Cell(currentRow, headerStartCol).Value = tableComment;
            worksheet.Cell(currentRow, headerStartCol).Style.Fill.BackgroundColor = XLColor.LightGreen;
            worksheet.Cell(currentRow, headerStartCol).Style.Font.Bold = true;
            
            worksheet.Cell(currentRow, headerStartCol + 1).Value = tableName;
            worksheet.Cell(currentRow, headerStartCol + 1).Style.Fill.BackgroundColor = XLColor.LightGreen;
            worksheet.Cell(currentRow, headerStartCol + 1).Style.Font.Bold = true;

            currentRow++;

            // 列头：列日文名、列英文名（绿色底色）
            int colHeaderRow = currentRow;
            worksheet.Cell(colHeaderRow, 1).Value = ""; // A列留空
            worksheet.Cell(colHeaderRow, 2).Value = ""; // B列留空（用于"更新前"/"更新後"）
            
            int colIndex = headerStartCol;
            foreach (var column in columns)
            {
                var columnComment = columnComments.TryGetValue(column, out var comment) ? comment : column;
                worksheet.Cell(colHeaderRow, colIndex).Value = columnComment; // 日文名
                worksheet.Cell(colHeaderRow, colIndex).Style.Fill.BackgroundColor = XLColor.LightGreen;
                worksheet.Cell(colHeaderRow, colIndex).Style.Font.Bold = true;
                
                colIndex++;
                worksheet.Cell(colHeaderRow, colIndex).Value = column; // 英文名
                worksheet.Cell(colHeaderRow, colIndex).Style.Fill.BackgroundColor = XLColor.LightGreen;
                worksheet.Cell(colHeaderRow, colIndex).Style.Font.Bold = true;
                
                colIndex++;
            }

            currentRow++;

            // 数据行：每两行显示一个比较结果
            foreach (var result in tableResults)
            {
                // 更新前行（B列：更新前）
                worksheet.Cell(currentRow, 2).Value = "更新前";
                worksheet.Cell(currentRow, 2).Style.Font.Bold = true;

                int dataCol = headerStartCol;
                foreach (var column in columns)
                {
                    object? value = null;
                    
                    // 优先从主键获取，然后从旧值获取
                    if (result.PrimaryKeyValues.TryGetValue(column, out var pkValue))
                    {
                        value = pkValue;
                    }
                    else if (result.OldValues.TryGetValue(column, out var oldValue))
                    {
                        value = oldValue;
                    }

                    // 新增场合：更新前行空白
                    if (result.Status == ComparisonStatus.Added)
                    {
                        value = null;
                    }

                    // 日文名列和英文名列都写入相同的值
                    worksheet.Cell(currentRow, dataCol).Value = value?.ToString() ?? "";
                    dataCol++;
                    worksheet.Cell(currentRow, dataCol).Value = value?.ToString() ?? "";
                    dataCol++;
                }

                currentRow++;

                // 更新後行（B列：更新後）
                string statusLabel = result.Status switch
                {
                    ComparisonStatus.Deleted => "更新後（削除）",
                    ComparisonStatus.Added => "更新後（追加）",
                    ComparisonStatus.Updated => "更新後（更新）",
                    _ => "更新後"
                };
                
                worksheet.Cell(currentRow, 2).Value = statusLabel;
                worksheet.Cell(currentRow, 2).Style.Font.Bold = true;

                dataCol = headerStartCol;

                foreach (var column in columns)
                {
                    object? value = null;
                    bool isChanged = false;

                    // 优先从主键获取，然后从新值获取
                    if (result.PrimaryKeyValues.TryGetValue(column, out var pkValue))
                    {
                        value = pkValue;
                    }
                    else if (result.NewValues.TryGetValue(column, out var newValue))
                    {
                        value = newValue;
                        
                        // 检查是否变更（更新场合）
                        if (result.Status == ComparisonStatus.Updated)
                        {
                            result.OldValues.TryGetValue(column, out var oldValue);
                            if (!Equals(oldValue, newValue))
                            {
                                isChanged = true;
                            }
                        }
                    }

                    // 删除场合：更新後行空白
                    if (result.Status == ComparisonStatus.Deleted)
                    {
                        value = null;
                    }

                    // 日文名列和英文名列都写入相同的值
                    worksheet.Cell(currentRow, dataCol).Value = value?.ToString() ?? "";
                    
                    // 如果是变更的字段，添加黄色底色（两列都标记）
                    if (isChanged)
                    {
                        worksheet.Cell(currentRow, dataCol).Style.Fill.BackgroundColor = XLColor.LightYellow;
                    }
                    
                    dataCol++;
                    worksheet.Cell(currentRow, dataCol).Value = value?.ToString() ?? "";
                    
                    if (isChanged)
                    {
                        worksheet.Cell(currentRow, dataCol).Style.Fill.BackgroundColor = XLColor.LightYellow;
                    }
                    
                    dataCol++;
                }

                currentRow++;
                currentRow++; // 空行分隔
            }

            currentRow++; // 表之间的空行
        }

        worksheet.Columns().AdjustToContents();
    }
}
