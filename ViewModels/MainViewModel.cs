using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using WslPostgreTool.Models;
using WslPostgreTool.Services;

namespace WslPostgreTool.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<DatabaseConnection> _connections = new();

    [ObservableProperty]
    private DatabaseConnection? _selectedConnection;

    [ObservableProperty]
    private ObservableCollection<string> _wslDistributions = new();

    [ObservableProperty]
    private string _logMessage = string.Empty;

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private DbConfigViewModel _dbConfigViewModel;

    [ObservableProperty]
    private ImportExportViewModel _importExportViewModel;

    [ObservableProperty]
    private CompareViewModel _compareViewModel;

    private readonly WslService _wslService = new();
    private readonly ConfigService _configService = new();

    public MainViewModel()
    {
        // ViewModels を初期化（MainViewModel への参照を渡す）
        DbConfigViewModel = new DbConfigViewModel(this);
        ImportExportViewModel = new ImportExportViewModel(this);
        CompareViewModel = new CompareViewModel(this);
        
        // 接続リストを共有
        DbConfigViewModel.Connections = Connections;
        ImportExportViewModel.Connections = Connections;
        CompareViewModel.Connections = Connections;
        
        LoadWslDistributions();
        LoadConnections();
    }

    private async void LoadWslDistributions()
    {
        var distros = await _wslService.GetWslDistributionsAsync();
        WslDistributions.Clear();
        foreach (var distro in distros)
        {
            WslDistributions.Add(distro);
        }
        DbConfigViewModel.WslDistributions = WslDistributions;
    }

    private void LoadConnections()
    {
        try
        {
            var connections = _configService.LoadConnections();
            Connections.Clear();
            foreach (var conn in connections)
            {
                Connections.Add(conn);
            }
        }
        catch (Exception ex)
        {
            LogMessage = $"[エラー] 設定の読み込みに失敗しました: {ex.Message}";
        }
    }

    public void SaveConnections()
    {
        try
        {
            _configService.SaveConnections(Connections.ToList());
        }
        catch (Exception ex)
        {
            LogMessage = $"[エラー] 設定の保存に失敗しました: {ex.Message}";
        }
    }

    public void AppendLog(string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        if (string.IsNullOrEmpty(LogMessage))
        {
            LogMessage = $"[{timestamp}] {message}";
        }
        else
        {
            LogMessage += $"\n[{timestamp}] {message}";
        }
    }
}
