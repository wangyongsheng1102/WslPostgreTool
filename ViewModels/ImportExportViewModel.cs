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

    [ObservableProperty]
    private string _logMessage = string.Empty;

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _exportFolderPath = string.Empty;

    [ObservableProperty]
    private string _importFolderPath = string.Empty;

    private readonly DatabaseService _databaseService = new();

    public ImportExportViewModel()
    {
    }

    [RelayCommand]
    private async Task LoadTables()
    {
        if (SelectedConnection == null)
        {
            LogMessage = "[エラー] 接続を選択してください。";
            return;
        }

        try
        {
            IsProcessing = true;
            LogMessage = "[処理中] テーブルリストを取得しています...";
            
            var tables = await _databaseService.GetTablesAsync(SelectedConnection.GetConnectionString());
            Tables.Clear();
            foreach (var table in tables)
            {
                Tables.Add(table);
            }

            LogMessage = $"[完了] {Tables.Count} 個のテーブルを取得しました。";
        }
        catch (Exception ex)
        {
            LogMessage = $"[エラー] テーブル取得に失敗しました: {ex.Message}";
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
    }

    [RelayCommand]
    private void DeselectAllTables()
    {
        foreach (var table in Tables)
        {
            table.IsSelected = false;
        }
    }

    [RelayCommand]
    private async Task ExportTables()
    {
        if (SelectedConnection == null)
        {
            LogMessage = "[エラー] 接続を選択してください。";
            return;
        }

        if (string.IsNullOrWhiteSpace(ExportFolderPath))
        {
            LogMessage = "[エラー] エクスポートフォルダを選択してください。";
            return;
        }

        var selectedTables = Tables.Where(t => t.IsSelected).ToList();
        if (selectedTables.Count == 0)
        {
            LogMessage = "[エラー] エクスポートするテーブルを選択してください。";
            return;
        }

        try
        {
            IsProcessing = true;
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var exportDir = Path.Combine(ExportFolderPath, timestamp);
            Directory.CreateDirectory(exportDir);

            LogMessage = $"[処理中] エクスポートを開始しています... ({selectedTables.Count} テーブル)";
            ProgressValue = 0;

            var connectionString = SelectedConnection.GetConnectionString();
            int completed = 0;

            foreach (var table in selectedTables)
            {
                var csvPath = Path.Combine(exportDir, $"{table.TableName}.csv");
                await _databaseService.ExportTableToCsvAsync(
                    connectionString,
                    table.SchemaName,
                    table.TableName,
                    csvPath,
                    new Progress<string>(msg => LogMessage = msg));

                completed++;
                ProgressValue = (int)(completed * 100 / selectedTables.Count);
            }

            LogMessage = $"[完了] {selectedTables.Count} 個のテーブルをエクスポートしました。保存先: {exportDir}";
        }
        catch (Exception ex)
        {
            LogMessage = $"[エラー] エクスポートに失敗しました: {ex.Message}";
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
            LogMessage = "[エラー] 接続を選択してください。";
            return;
        }

        if (string.IsNullOrWhiteSpace(ImportFolderPath))
        {
            LogMessage = "[エラー] インポートフォルダを選択してください。";
            return;
        }

        var csvFiles = Directory.GetFiles(ImportFolderPath, "*.csv");
        if (csvFiles.Length == 0)
        {
            LogMessage = "[エラー] CSV ファイルが見つかりません。";
            return;
        }

        try
        {
            IsProcessing = true;
            LogMessage = $"[処理中] インポートを開始しています... ({csvFiles.Length} ファイル)";
            ProgressValue = 0;

            var connectionString = SelectedConnection.GetConnectionString();
            int completed = 0;

            foreach (var csvFile in csvFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(csvFile);
                // テーブル名を推測（スキーマ名が含まれている場合は分割）
                var parts = fileName.Split('.');
                string schemaName, tableName;
                
                if (parts.Length == 2)
                {
                    schemaName = parts[0];
                    tableName = parts[1];
                }
                else
                {
                    schemaName = "public";
                    tableName = fileName;
                }

                await _databaseService.ImportTableFromCsvAsync(
                    connectionString,
                    schemaName,
                    tableName,
                    csvFile,
                    new Progress<string>(msg => LogMessage = msg));

                completed++;
                ProgressValue = (int)(completed * 100 / csvFiles.Length);
            }

            LogMessage = $"[完了] {csvFiles.Length} 個のファイルをインポートしました。";
        }
        catch (Exception ex)
        {
            LogMessage = $"[エラー] インポートに失敗しました: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            ProgressValue = 0;
        }
    }
}

