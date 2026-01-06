using ClosedXML.Excel;
using WslPostgreTool.Models;

namespace WslPostgreTool.Services;

/// <summary>
/// Excel エクスポートサービス
/// </summary>
public class ExcelExportService
{
    /// <summary>
    /// 比較結果を Excel にエクスポート
    /// </summary>
    public void ExportComparisonResults(string filePath, List<RowComparisonResult> results)
    {
        using var workbook = new XLWorkbook();
        
        // ステータスごとにシートを作成
        var deletedResults = results.Where(r => r.Status == ComparisonStatus.Deleted).ToList();
        var addedResults = results.Where(r => r.Status == ComparisonStatus.Added).ToList();
        var updatedResults = results.Where(r => r.Status == ComparisonStatus.Updated).ToList();

        if (deletedResults.Any())
        {
            CreateSheet(workbook, "削除", deletedResults);
        }

        if (addedResults.Any())
        {
            CreateSheet(workbook, "追加", addedResults);
        }

        if (updatedResults.Any())
        {
            CreateSheet(workbook, "更新", updatedResults);
        }

        // サマリーシート
        var summarySheet = workbook.Worksheets.Add("サマリー");
        summarySheet.Cell(1, 1).Value = "ステータス";
        summarySheet.Cell(1, 2).Value = "件数";
        
        summarySheet.Cell(2, 1).Value = "削除";
        summarySheet.Cell(2, 2).Value = deletedResults.Count;
        
        summarySheet.Cell(3, 1).Value = "追加";
        summarySheet.Cell(3, 2).Value = addedResults.Count;
        
        summarySheet.Cell(4, 1).Value = "更新";
        summarySheet.Cell(4, 2).Value = updatedResults.Count;

        summarySheet.Columns().AdjustToContents();

        workbook.SaveAs(filePath);
    }

    private static void CreateSheet(XLWorkbook workbook, string sheetName, List<RowComparisonResult> results)
    {
        var worksheet = workbook.Worksheets.Add(sheetName);

        // ヘッダー行を決定（全結果から全カラムを収集）
        var allColumns = new HashSet<string>();
        foreach (var result in results)
        {
            foreach (var key in result.PrimaryKeyValues.Keys)
                allColumns.Add($"主キー:{key}");
            
            foreach (var key in result.OldValues.Keys)
                allColumns.Add($"旧値:{key}");
            
            foreach (var key in result.NewValues.Keys)
                allColumns.Add($"新値:{key}");
        }

        var columns = allColumns.OrderBy(c => c).ToList();

        // ヘッダー行
        for (int i = 0; i < columns.Count; i++)
        {
            worksheet.Cell(1, i + 1).Value = columns[i];
        }

        // データ行
        int row = 2;
        foreach (var result in results)
        {
            int col = 1;
            foreach (var column in columns)
            {
                object? value = null;
                
                if (column.StartsWith("主キー:"))
                {
                    var key = column.Substring(4);
                    result.PrimaryKeyValues.TryGetValue(key, out value);
                }
                else if (column.StartsWith("旧値:"))
                {
                    var key = column.Substring(3);
                    result.OldValues.TryGetValue(key, out value);
                }
                else if (column.StartsWith("新値:"))
                {
                    var key = column.Substring(3);
                    result.NewValues.TryGetValue(key, out value);
                }

                worksheet.Cell(row, col).Value = value?.ToString() ?? "";
                col++;
            }
            row++;
        }

        worksheet.Columns().AdjustToContents();
    }
}

