using CommunityToolkit.Mvvm.ComponentModel;

namespace WslPostgreTool.Models;

/// <summary>
/// CSV ファイル情報モデル
/// </summary>
public partial class CsvFileInfo : ObservableObject
{
    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private bool _isSelected = false;

    public CsvFileInfo(string fileName)
    {
        FileName = fileName;
    }
}

