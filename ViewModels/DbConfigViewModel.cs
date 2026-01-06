using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WslPostgreTool.Models;
using WslPostgreTool.Services;

namespace WslPostgreTool.ViewModels;

public partial class DbConfigViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<DatabaseConnection> _connections = new();

    [ObservableProperty]
    private DatabaseConnection? _selectedConnection;

    [ObservableProperty]
    private ObservableCollection<string> _wslDistributions = new();

    public DbConfigViewModel()
    {
        LoadWslDistributions();
    }

    private readonly WslService _wslService = new();

    private async void LoadWslDistributions()
    {
        var distros = await _wslService.GetWslDistributionsAsync();
        WslDistributions.Clear();
        foreach (var distro in distros)
        {
            WslDistributions.Add(distro);
        }
    }

    [RelayCommand]
    private void AddConnection()
    {
        Connections.Add(new DatabaseConnection
        {
            ConfigurationName = $"設定{Connections.Count + 1}",
            Host = "localhost",
            Port = 5432
        });
    }

    [RelayCommand]
    private void RemoveConnection(DatabaseConnection? connection)
    {
        if (connection != null)
        {
            Connections.Remove(connection);
        }
    }

    [RelayCommand]
    private async Task RefreshWslDistributions()
    {
        var distros = await _wslService.GetWslDistributionsAsync();
        WslDistributions.Clear();
        foreach (var distro in distros)
        {
            WslDistributions.Add(distro);
        }
    }
}

