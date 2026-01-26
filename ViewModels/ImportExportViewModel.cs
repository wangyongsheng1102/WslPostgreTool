using System;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WslPostgreTool.Models;
using WslPostgreTool.Services;

namespace WslPostgreTool.ViewModels;

public partial class ImportExportViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<DatabaseConnection> _connections = new();

    [ObservableProperty]
    private DatabaseConnection? _selectedConnection;

    [ObservableProperty]
    private ObservableCollection<TableInfo> _tables = new();
    
    public int SelectedTableCount => Tables.Count(f => f.IsSelected);

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _exportFolderPath = string.Empty;

    [ObservableProperty]
    private string _importFolderPath = string.Empty;

    private readonly DatabaseService _databaseService = new();
    private readonly MainViewModel _mainViewModel;

    public ImportExportViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
    }

    [RelayCommand]
    private async Task LoadTables()
    {
        if (SelectedConnection == null)
        {
            _mainViewModel.AppendLog("[エラー] 接続を選択してください。");
            return;
        }

        try
        {
            IsProcessing = true;
            _mainViewModel.AppendLog("[処理中] テーブルリストを取得しています...");
            
            var tables = await _databaseService.GetTablesAsync(SelectedConnection.GetConnectionString());
            Tables.Clear();
            foreach (var table in tables)
            {
                table.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(TableInfo.IsSelected))
                    {
                        OnPropertyChanged(nameof(SelectedTableCount));
                    }
                };
                Tables.Add(table);
            }

            OnPropertyChanged(nameof(SelectedTableCount));

            _mainViewModel.AppendLog($"[完了] {Tables.Count} 個のテーブルを取得しました。", LogLevel.Success);
        }
        catch (Exception ex)
        {
            _mainViewModel.AppendLog($"[エラー] テーブル取得に失敗しました: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private void SelectAllTables()
    {
        foreach (var table in Tables)
        {
            table.IsSelected = true;
        }
        OnPropertyChanged(nameof(SelectedTableCount));
    }

    [RelayCommand]
    private void DeselectAllTables()
    {
        foreach (var table in Tables)
        {
            table.IsSelected = false;
        }
        OnPropertyChanged(nameof(SelectedTableCount));
    }

    [RelayCommand]
    private async Task ExportTables()
    {
        if (SelectedConnection == null)
        {
            _mainViewModel.AppendLog("[エラー] 接続を選択してください。", LogLevel.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(ExportFolderPath))
        {
            _mainViewModel.AppendLog("[エラー] エクスポートフォルダを選択してください。", LogLevel.Error);
            return;
        }

        var selectedTables = Tables.Where(t => t.IsSelected).ToList();
        if (selectedTables.Count == 0)
        {
            _mainViewModel.AppendLog("[エラー] エクスポートするテーブルを選択してください。", LogLevel.Error);
            return;
        }

        try
        {
            IsProcessing = true;
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var exportDir = Path.Combine(ExportFolderPath, timestamp);
            Directory.CreateDirectory(exportDir);

            _mainViewModel.AppendLog($"[処理中] エクスポートを開始しています... ({selectedTables.Count} テーブル)", LogLevel.Info);
            ProgressValue = 0;

            var connectionString = SelectedConnection.GetConnectionString();
            int completed = 0;

            foreach (var table in selectedTables)
            {
                // 文件名不再包含 schema，只使用表名
                var csvPath = Path.Combine(exportDir, $"{table.TableName}.csv");
                await _databaseService.ExportTableToCsvAsync(
                    connectionString,
                    table.SchemaName,
                    table.TableName,
                    csvPath,
                    new Progress<string>(msg => _mainViewModel.AppendLog(msg)));

                completed++;
                ProgressValue = (int)(completed * 100 / selectedTables.Count);
            }

            _mainViewModel.AppendLog($"[完了] {selectedTables.Count} 個のテーブルをエクスポートしました。保存先: {exportDir}", LogLevel.Success);
        }
        catch (Exception ex)
        {
            _mainViewModel.AppendLog($"[エラー] エクスポートに失敗しました: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsProcessing = false;
            ProgressValue = 0;
        }
    }

    private Window? GetMainWindow()
    {
        return (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    }

    [RelayCommand]
    private async Task SelectExportFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "エクスポートフォルダを選択"
        };

        var window = GetMainWindow();
        if (window != null)
        {
            var result = await dialog.ShowAsync(window);
            if (!string.IsNullOrWhiteSpace(result))
            {
                ExportFolderPath = result;
            }
        }
    }

    [RelayCommand]
    private async Task SelectImportFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "インポートフォルダを選択"
        };

        var window = GetMainWindow();
        if (window != null)
        {
            var result = await dialog.ShowAsync(window);
            if (!string.IsNullOrWhiteSpace(result))
            {
                ImportFolderPath = result;
            }
        }
    }

    [RelayCommand]
    private async Task ImportTables()
    {
        if (SelectedConnection == null)
        {
            _mainViewModel.AppendLog("[エラー] 接続を選択してください。", LogLevel.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(ImportFolderPath))
        {
            _mainViewModel.AppendLog("[エラー] インポートフォルダを選択してください。", LogLevel.Error);
            return;
        }

        var csvFiles = Directory.GetFiles(ImportFolderPath, "*.csv", SearchOption.AllDirectories);
        if (csvFiles.Length == 0)
        {
            _mainViewModel.AppendLog("[エラー] CSV ファイルが見つかりません。", LogLevel.Error);
            return;
        }

        try
        {
            IsProcessing = true;
            _mainViewModel.AppendLog($"[処理中] インポートを開始しています... ({csvFiles.Length} ファイル)", LogLevel.Info);
            ProgressValue = 0;

            var connectionString = SelectedConnection.GetConnectionString();
            int completed = 0;

            // 根据 username 确定 schema
            string schemaName = GetSchemaFromUsername(SelectedConnection.User);
            
            foreach (var csvFile in csvFiles)
            {
                // 文件名不再包含 schema，直接使用文件名作为表名
                var tableName = Path.GetFileNameWithoutExtension(csvFile);

                _mainViewModel.AppendLog($"[処理中] {csvFile} をインポートしています...", LogLevel.Info);
                
                await _databaseService.ImportTableFromCsvAsync(
                    connectionString,
                    schemaName,
                    tableName,
                    csvFile,
                    new Progress<string>(msg => _mainViewModel.AppendLog(msg)));

                completed++;
                ProgressValue = (int)(completed * 100 / csvFiles.Length);
            }

            _mainViewModel.AppendLog($"[完了] {csvFiles.Length} 個のファイルをインポートしました。", LogLevel.Success);
        }
        catch (Exception ex)
        {
            _mainViewModel.AppendLog($"[エラー] インポートに失敗しました: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsProcessing = false;
            ProgressValue = 0;
        }
    }
    
    /// <summary>
    /// 根据 username 确定 schema
    /// </summary>
    private static string GetSchemaFromUsername(string username)
    {
        if (string.IsNullOrEmpty(username))
            return "public";
            
        if (username.StartsWith("cis", StringComparison.OrdinalIgnoreCase))
        {
            return "unisys";
        }
        else if (username.StartsWith("order", StringComparison.OrdinalIgnoreCase))
        {
            return "public";
        }
        else if (username.StartsWith("portal", StringComparison.OrdinalIgnoreCase))
        {
            return "public";
        }
        
        return "public";
    }
}
