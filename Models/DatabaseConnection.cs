using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WslPostgreTool.Models;

/// <summary>
/// データベース接続設定モデル
/// </summary>
public class DatabaseConnection : INotifyPropertyChanged
{
    private string _configurationName = string.Empty;
    private string _host = "localhost";
    private int _port = 5432;
    private string _database = string.Empty;
    private string _user = string.Empty;
    private string _password = string.Empty;
    private string _wslDistro = string.Empty;

    public string ConfigurationName
    {
        get => _configurationName;
        set => SetProperty(ref _configurationName, value);
    }

    public string Host
    {
        get => _host;
        set => SetProperty(ref _host, value);
    }

    public int Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

    public string Database
    {
        get => _database;
        set => SetProperty(ref _database, value);
    }

    public string User
    {
        get => _user;
        set => SetProperty(ref _user, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public string WslDistro
    {
        get => _wslDistro;
        set => SetProperty(ref _wslDistro, value);
    }

    /// <summary>
    /// 接続文字列を生成
    /// </summary>
    public string GetConnectionString()
    {
        return $"Host={Host};Port={Port};Database={Database};Username={User};Password={Password}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

