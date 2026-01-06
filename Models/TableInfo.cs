namespace WslPostgreTool.Models;

/// <summary>
/// テーブル情報モデル
/// </summary>
public class TableInfo
{
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string FullName => $"{SchemaName}.{TableName}";
    public bool IsSelected { get; set; } = true;
    public long RowCount { get; set; }
}

