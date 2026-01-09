using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using WslPostgreTool.Models;
using WslPostgreTool.Services;

namespace WslPostgreTool.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _appVersion = "1.0.0";
    [ObservableProperty]
    private string _appAuthor = "wangys";
    
    [ObservableProperty]
    private ObservableCollection<DatabaseConnection> _connections = new();

    [ObservableProperty]
    private DatabaseConnection? _selectedConnection;

    [ObservableProperty]
    private ObservableCollection<string> _wslDistributions = new();

    [ObservableProperty]
    private string _logMessage = string.Empty;
    
    // 添加新的 LogMessages 集合用于 ItemsControl（支持分级）
    [ObservableProperty]
    private ObservableCollection<LogEntry> _logMessages = new();

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
    
    // 清空日志命令
    [RelayCommand]
    private void ClearLogs()
    {
        ClearLogsFunc();
    }
    
    // 添加清空日志的方法
    public void ClearLogsFunc()
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogMessages.Clear();
            LogMessage = string.Empty;
        });
    }
    
    // 添加滚动事件
    public event Action? RequestScrollToBottom;
    
    // 修改 AppendLog 方法（支持分级）
    public void AppendLog(string message, LogLevel level = LogLevel.Info)
    {
        // 在 UI 线程更新
        Dispatcher.UIThread.Post(() =>
        {
            var logEntry = new LogEntry(message, level);
            
            // 1. 更新 LogMessages 集合（用于 ItemsControl）
            LogMessages.Add(logEntry);
            
            // 2. 可选：保持 LogMessage 字符串（用于向后兼容）
            var logText = $"[{logEntry.Timestamp}] {message}";
            if (string.IsNullOrEmpty(LogMessage))
            {
                LogMessage = logText;
            }
            else
            {
                LogMessage += $"\n{logText}";
            }
            
            // 限制日志数量（防止内存无限增长）
            const int MAX_LOG_ENTRIES = 1000;
            if (LogMessages.Count > MAX_LOG_ENTRIES)
            {
                // 移除最旧的日志
                LogMessages.RemoveAt(0);
                
                // 重新构建 LogMessage 字符串（只保留最近1000条）
                var recentLogs = LogMessages.TakeLast(1000);
                LogMessage = string.Join("\n", recentLogs.Select(l => $"[{l.Timestamp}] {l.Message}"));
                RequestScrollToBottom?.Invoke();
            }
            else
            {
                RequestScrollToBottom?.Invoke();
            }
        }, DispatcherPriority.Background);
    }
    
    // 向后兼容：无分级的AppendLog方法
    public void AppendLog(string message)
    {
        AppendLog(message, LogLevel.Info);
    }
}
