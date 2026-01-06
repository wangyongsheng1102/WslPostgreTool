using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
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
    private DbConfigViewModel _dbConfigViewModel = new();

    [ObservableProperty]
    private ImportExportViewModel _importExportViewModel = new();

    [ObservableProperty]
    private CompareViewModel _compareViewModel = new();

    private readonly WslService _wslService = new();

    public MainViewModel()
    {
        LoadWslDistributions();
        
        // 接続リストを共有
        DbConfigViewModel.Connections = Connections;
        ImportExportViewModel.Connections = Connections;
        CompareViewModel.Connections = Connections;
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
}

