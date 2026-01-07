using CommunityToolkit.Mvvm.ComponentModel;

namespace WslPostgreTool.Models;

/// <summary>
/// テーブル情報モデル
/// </summary>
public partial class TableInfo:ObservableObject
{
    
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string FullName => $"{SchemaName}.{TableName}";
    [ObservableProperty]
    public bool isSelected = true;
    public long RowCount { get; set; }
}

