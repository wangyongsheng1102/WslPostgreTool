using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WslPostgreTool.Models;

/// <summary>
/// データベース接続設定モデル
/// </summary>
public partial class DatabaseConnection : ObservableObject
{
    [ObservableProperty]
    private string _configurationName = string.Empty;
    [ObservableProperty]
    private string _host = "localhost";
    [ObservableProperty]
    private int _port = 5432;
    [ObservableProperty]
    private string _database = string.Empty;
    [ObservableProperty]
    private string _user = string.Empty;
    [ObservableProperty]
    private string _password = string.Empty;
    [ObservableProperty]
    private string _wslDistro = string.Empty;

    /// <summary>
    /// 接続文字列を生成
    /// </summary>
    public string GetConnectionString()
    {
        return $"Host={Host};Port={Port};Database={Database};Username={User};Password={Password}";
    }

}

