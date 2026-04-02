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

    /// <summary>
    /// 接続先のスキーマ一覧（接続選択後に SQL で取得）
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<string> _schemas = new();

    /// <summary>
    /// 選択中のスキーマ（エクスポート・インポート共通）
    /// </summary>
    [ObservableProperty]
    private string? _selectedSchema;

    private readonly DatabaseService _databaseService = new();
    private readonly MainViewModel _mainViewModel;

    /// <summary>
    /// Progress 消息根据前缀映射为 LogLevel，再写入日志（用于正确显示红/黄等颜色）
    /// </summary>
    private void AppendProgressLog(string message)
    {
        var level = message.StartsWith("[エラー]", StringComparison.Ordinal) ? LogLevel.Error
            : message.StartsWith("[スキップ]", StringComparison.Ordinal) ? LogLevel.Error
            : message.StartsWith("[警告]", StringComparison.Ordinal) ? LogLevel.Warning
            : message.StartsWith("[完了]", StringComparison.Ordinal) ? LogLevel.Success
            : message.StartsWith("[情報]", StringComparison.Ordinal) ? LogLevel.Info
            : LogLevel.Info;
        _mainViewModel.AppendLog(message, level);
    }

    public ImportExportViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
    }

    partial void OnSelectedConnectionChanged(DatabaseConnection? value)
    {
        Schemas.Clear();
        SelectedSchema = null;
        Tables.Clear();
        OnPropertyChanged(nameof(SelectedTableCount));
        if (value != null)
            _ = LoadSchemasAsync();
    }

    /// <summary>
    /// 接続先のスキーマ一覧を取得してドロップダウンに反映（接続選択時に自動実行）
    /// </summary>
    private async Task LoadSchemasAsync()
    {
        var connection = SelectedConnection;
        if (connection == null) return;

        try
        {
            var schemaList = await _databaseService.GetSchemasAsync(connection.GetConnectionString());
            // 選択が変わっていたら反映しない
            if (SelectedConnection != connection) return;

            Schemas.Clear();
            foreach (var s in schemaList)
                Schemas.Add(s);
            if (Schemas.Count > 0 && (string.IsNullOrEmpty(SelectedSchema) || !Schemas.Contains(SelectedSchema)))
                SelectedSchema = Schemas[0];

            _mainViewModel.AppendLog($"[完了] スキーマ一覧を取得しました。（{Schemas.Count} 件）", LogLevel.Success);
        }
        catch (Exception ex)
        {
            if (SelectedConnection == connection)
                _mainViewModel.AppendLog($"[エラー] スキーマ一覧の取得に失敗しました: {ex.Message}", LogLevel.Error);
        }
    }

    [RelayCommand]
    private async Task LoadTables()
    {
        if (SelectedConnection == null)
        {
            _mainViewModel.AppendLog("[エラー] 接続を選択してください。");
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedSchema))
        {
            _mainViewModel.AppendLog("[エラー] スキーマを選択してください。", LogLevel.Error);
            return;
        }

        try
        {
            IsProcessing = true;
            _mainViewModel.AppendLog("[処理中] テーブルリストを取得しています...");
            var tables = await _databaseService.GetTablesAsync(SelectedConnection.GetConnectionString(), SelectedSchema!);
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
                    new Progress<string>(msg => AppendProgressLog(msg)));

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

        if (string.IsNullOrWhiteSpace(SelectedSchema))
        {
            _mainViewModel.AppendLog("[エラー] スキーマを選択してください。（テーブルリストを読み込みでスキーマを取得）", LogLevel.Error);
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
            var schemaName = SelectedSchema!;
            int successCount = 0, skipCount = 0, failCount = 0;
            int processed = 0;

            foreach (var csvFile in csvFiles)
            {
                var tableName = Path.GetFileNameWithoutExtension(csvFile);
                _mainViewModel.AppendLog($"[処理中] {csvFile} をインポートしています...", LogLevel.Info);

                try
                {
                    var (success, skipped) = await _databaseService.ImportTableFromCsvAsync(
                        connectionString,
                        schemaName,
                        tableName,
                        csvFile,
                        new Progress<string>(msg => AppendProgressLog(msg)));

                    if (skipped)
                        skipCount++;
                    else if (success)
                        successCount++;
                }
                catch
                {
                    failCount++;
                }

                processed++;
                ProgressValue = (int)(processed * 100 / csvFiles.Length);
            }

            if (failCount > 0 || skipCount > 0)
            {
                _mainViewModel.AppendLog(
                    $"[エラー] インポート異常。成功 {successCount} 件、スキップ {skipCount} 件、失敗 {failCount} 件。",
                    LogLevel.Error);
            }
            else
            {
                _mainViewModel.AppendLog($"[完了] {csvFiles.Length} 個のファイルをインポートしました。", LogLevel.Success);
            }
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
}