using System.Collections.Generic;

namespace WslPostgreTool.Models;

/// <summary>
/// データ比較結果モデル
/// </summary>
public enum ComparisonStatus
{
    /// <summary>削除 - 旧库有，新库无</summary>
    Deleted,
    /// <summary>追加 - 旧库无，新库有</summary>
    Added,
    /// <summary>更新 - 主键存在但哈希值不同</summary>
    Updated,
    /// <summary>変更なし</summary>
    Unchanged
}

/// <summary>
/// 行比較結果
/// </summary>
public class RowComparisonResult
{
    public string TableName { get; set; } = string.Empty;
    public ComparisonStatus Status { get; set; }
    public Dictionary<string, object?> OldValues { get; set; } = new();
    public Dictionary<string, object?> NewValues { get; set; } = new();
    public Dictionary<string, object?> PrimaryKeyValues { get; set; } = new();
}

