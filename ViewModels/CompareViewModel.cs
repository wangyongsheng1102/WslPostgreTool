using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WslPostgreTool.Models;
using WslPostgreTool.Services;

namespace WslPostgreTool.ViewModels;

public partial class CompareViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<DatabaseConnection> _connections = new();

    [ObservableProperty]
    private DatabaseConnection? _selectedConnection;

    [ObservableProperty]
    private string _baseFolderPath = string.Empty;

    [ObservableProperty]
    private string _oldFolderPath = string.Empty;

    [ObservableProperty]
    private string _newFolderPath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<CsvFileInfo> _csvFileInfos = new();

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _exportFilePath = string.Empty;
    
    [ObservableProperty]
    private bool _accessLogChecked = false;

    public int SelectedCsvFileCount => CsvFileInfos.Count(f => f.IsSelected);

    private readonly CsvCompareService _csvCompareService = new();
    private readonly DatabaseService _databaseService = new();
    private readonly ExcelExportService _excelService = new();
    private readonly MainViewModel _mainViewModel;

    public CompareViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
    }

    [RelayCommand]
    private async Task SelectBaseFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "更新前フォルダを選択"
        };

        var window = GetMainWindow();
        if (window != null)
        {
            var result = await dialog.ShowAsync(window);
            if (!string.IsNullOrWhiteSpace(result))
            {
                BaseFolderPath = result;
                LoadCsvFilePairs();
            }
        }
    }

    [RelayCommand]
    private async Task SelectOldFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "旧フォルダを選択"
        };

        var window = GetMainWindow();
        if (window != null)
        {
            var result = await dialog.ShowAsync(window);
            if (!string.IsNullOrWhiteSpace(result))
            {
                OldFolderPath = result;
                LoadCsvFilePairs();
            }
        }
    }

    [RelayCommand]
    private async Task SelectNewFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "新フォルダを選択"
        };

        var window = GetMainWindow();
        if (window != null)
        {
            var result = await dialog.ShowAsync(window);
            if (!string.IsNullOrWhiteSpace(result))
            {
                NewFolderPath = result;
                LoadCsvFilePairs();
            }
        }
    }

    private void LoadCsvFilePairs()
    {
        if (string.IsNullOrWhiteSpace(BaseFolderPath) || 
            string.IsNullOrWhiteSpace(OldFolderPath) || 
            string.IsNullOrWhiteSpace(NewFolderPath))
        {
            return;
        }

        try
        {
            var baseFiles = Directory.GetFiles(BaseFolderPath, "*.csv", SearchOption.AllDirectories)
                .Select(f => Path.GetFileName(f))
                .ToHashSet();

            var oldFiles = Directory.GetFiles(OldFolderPath, "*.csv", SearchOption.AllDirectories)
                .Select(f => Path.GetFileName(f))
                .ToHashSet();

            var newFiles = Directory.GetFiles(NewFolderPath, "*.csv", SearchOption.AllDirectories)
                .Select(f => Path.GetFileName(f))
                .ToHashSet();

            // 3つのフォルダに共通するファイルを検出
            var commonFiles = baseFiles.Intersect(oldFiles).Intersect(newFiles).OrderBy(f => f).ToList();

            CsvFileInfos.Clear();
            foreach (var file in commonFiles)
            {
                var fileInfo = new CsvFileInfo(file);
                fileInfo.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(CsvFileInfo.IsSelected))
                    {
                        OnPropertyChanged(nameof(SelectedCsvFileCount));
                    }
                };
                CsvFileInfos.Add(fileInfo);
            }

            OnPropertyChanged(nameof(SelectedCsvFileCount));
            _mainViewModel.AppendLog($"{CsvFileInfos.Count} 個の共通 CSV ファイルを検出しました。", LogLevel.Info);
        }
        catch (Exception ex)
        {
            _mainViewModel.AppendLog($"[エラー] ファイルリストの取得に失敗しました: {ex.Message}", LogLevel.Error);
        }
    }

    [RelayCommand]
    private void SelectAllCsvFiles()
    {
        foreach (var fileInfo in CsvFileInfos)
        {
            fileInfo.IsSelected = true;
        }
        OnPropertyChanged(nameof(SelectedCsvFileCount));
    }

    [RelayCommand]
    private void DeselectAllCsvFiles()
    {
        foreach (var fileInfo in CsvFileInfos)
        {
            fileInfo.IsSelected = false;
        }
        OnPropertyChanged(nameof(SelectedCsvFileCount));
    }

    [RelayCommand]
    private async Task CompareCsvFiles()
    {
        LoadCsvFilePairs();
        
        if (SelectedConnection == null)
        {
            _mainViewModel.AppendLog("[エラー] データベース接続を選択してください。", LogLevel.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(BaseFolderPath) || 
            string.IsNullOrWhiteSpace(OldFolderPath) || 
            string.IsNullOrWhiteSpace(NewFolderPath))
        {
            _mainViewModel.AppendLog("[エラー] 更新前フォルダ、旧フォルダ、新フォルダのすべてを選択してください。", LogLevel.Error);
            return;
        }

        // すべてのCSVファイルを比較（選択機能は無効化）
        var allFiles = CsvFileInfos.ToList();
        if (allFiles.Count == 0)
        {
            _mainViewModel.AppendLog("[エラー] 比較する CSV ファイルが見つかりません。", LogLevel.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(ExportFilePath))
        {
            _mainViewModel.AppendLog("[エラー] エクスポートファイルパスを指定してください。", LogLevel.Error);
            return;
        }

        try
        {
            IsProcessing = true;
            _mainViewModel.AppendLog($"[処理中] {allFiles.Count} 個の CSV ファイルの比較を開始しています...", LogLevel.Info);
            ProgressValue = 0;

            if (IsFileLocked(ExportFilePath))
            {
                _mainViewModel.AppendLog("Excel ファイルは LOCK していますので、チェックしてください...", LogLevel.Error);
                return;
            }
            
            await Task.Run(async () => {

                var connectionString = SelectedConnection.GetConnectionString();
                var baseVsOldResults = new List<RowComparisonResult>();
                var baseVsNewResults = new List<RowComparisonResult>();
                int completed = 0;

                foreach (var csvFileInfo in allFiles)
                {
                    if (!AccessLogChecked && csvFileInfo.FileName.Contains("access_log.csv", StringComparison.OrdinalIgnoreCase))
                    {
                        _mainViewModel.AppendLog($"[スキップ] access_log.csv の比較はスキップされました。", LogLevel.Warning);
                        completed++;
                        ProgressValue = (completed * 200) / (allFiles.Count * 2);
                        continue;
                    }
                    var csvFileName = csvFileInfo.FileName;
                    _mainViewModel.AppendLog($"[処理中] {csvFileName} を比較しています...", LogLevel.Info);

                    var baseCsvPath = FindCsvFile(BaseFolderPath, csvFileName);
                    var oldCsvPath = FindCsvFile(OldFolderPath, csvFileName);
                    var newCsvPath = FindCsvFile(NewFolderPath, csvFileName);

                    if (baseCsvPath == null || oldCsvPath == null || newCsvPath == null)
                    {
                        _mainViewModel.AppendLog($"[警告] {csvFileName} が見つかりません。スキップします。", LogLevel.Warning);
                        continue;
                    }

                    // 文件名不再包含 schema，直接使用文件名作为表名
                    // 根据 username 确定 schema
                    var tableName = Path.GetFileNameWithoutExtension(csvFileName);
                    string schemaName = GetSchemaFromUsername(SelectedConnection.User);

                    // データベースから主キーを取得（失敗した場合はnullを返す）
                    List<string>? primaryKeys = null;
                    try
                    {
                        primaryKeys = await _databaseService.GetPrimaryKeyColumnsAsync(
                            connectionString,
                            schemaName,
                            tableName);
                        
                        if (primaryKeys.Count > 0)
                        {
                            _mainViewModel.AppendLog($"[情報] テーブル '{schemaName}.{tableName}' の主キー: {string.Join(", ", primaryKeys)}", LogLevel.Info);
                        }
                        else
                        {
                            _mainViewModel.AppendLog($"[情報] テーブル '{schemaName}.{tableName}' に主キーがありません。整行比較モードを使用します。", LogLevel.Info);
                        }
                    }
                    catch (Exception ex)
                    {
                        _mainViewModel.AppendLog($"[警告] テーブル '{schemaName}.{tableName}' の主キー取得に失敗しました: {ex.Message}。整行比較モードを使用します。", LogLevel.Warning);
                    }

                    // Base vs Old の比較
                    var fileProgress1 = new Progress<(int current, int total, string message)>(p =>
                    {
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            var overallProgress = (completed * 200 + p.current) / (allFiles.Count * 2);
                            ProgressValue = overallProgress;
                            _mainViewModel.AppendLog($"[{csvFileName} - Base vs Old] {p.message}", LogLevel.Info);
                        });
                    });

                    var results1 = await _csvCompareService.CompareCsvFilesAsync(
                        baseCsvPath,
                        oldCsvPath,
                        primaryKeys,
                        connectionString,
                        schemaName,
                        tableName,
                        fileProgress1);

                    baseVsOldResults.AddRange(results1);

                    // Base vs New の比較
                    var fileProgress2 = new Progress<(int current, int total, string message)>(p =>
                    {
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            var overallProgress = (completed * 200 + 100 + p.current) / (allFiles.Count * 2);
                            ProgressValue = overallProgress;
                            _mainViewModel.AppendLog($"[{csvFileName} - Base vs New] {p.message}", LogLevel.Info);
                        });
                    });

                    var results2 = await _csvCompareService.CompareCsvFilesAsync(
                        baseCsvPath,
                        newCsvPath,
                        primaryKeys,
                        connectionString,
                        schemaName,
                        tableName,
                        fileProgress2);

                    baseVsNewResults.AddRange(results2);

                    completed++;
                    ProgressValue = (completed * 200) / (allFiles.Count * 2);
                }

                // Excel にエクスポート
                _mainViewModel.AppendLog("[処理中] Excel ファイルを生成しています...", LogLevel.Info);
                await Task.Run(() =>
                {
                    _excelService.ExportComparisonResults(ExportFilePath, baseVsOldResults, baseVsNewResults, connectionString);
                });

                var oldDeletedCount = baseVsOldResults.Count(r => r.Status == ComparisonStatus.Deleted);
                var oldAddedCount = baseVsOldResults.Count(r => r.Status == ComparisonStatus.Added);
                var oldUpdatedCount = baseVsOldResults.Count(r => r.Status == ComparisonStatus.Updated);

                var newDeletedCount = baseVsNewResults.Count(r => r.Status == ComparisonStatus.Deleted);
                var newAddedCount = baseVsNewResults.Count(r => r.Status == ComparisonStatus.Added);
                var newUpdatedCount = baseVsNewResults.Count(r => r.Status == ComparisonStatus.Updated);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _mainViewModel.AppendLog($"[完了] 比較が完了しました。", LogLevel.Success);
                    _mainViewModel.AppendLog($"更新前 vs 旧: 削除={oldDeletedCount}, 追加={oldAddedCount}, 更新={oldUpdatedCount}", LogLevel.Info);
                    _mainViewModel.AppendLog($"更新前 vs 新: 削除={newDeletedCount}, 追加={newAddedCount}, 更新={newUpdatedCount}", LogLevel.Info);
                    _mainViewModel.AppendLog($"結果を {ExportFilePath} に保存しました。", LogLevel.Success);
                });
            });
        }
        catch (Exception ex)
        {
            _mainViewModel.AppendLog($"[エラー] 比較に失敗しました: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsProcessing = false;
            ProgressValue = 0;
        }
    }
    
    // 辅助方法：检查文件是否被锁定
    private bool IsFileLocked(string filePath)
    {
        try
        {
            // 如果文件不存在，肯定没有被锁定
            if (!File.Exists(filePath))
                return false;
            
            // 尝试以读写方式打开文件，如果成功则说明没有被锁定
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                // 文件没有被锁定
                return false;
            }
        }
        catch (IOException)
        {
            // 文件被锁定或其他IO错误
            return true;
        }
        catch (Exception)
        {
            // 其他异常
            return true;
        }
    }

    private string? FindCsvFile(string folderPath, string fileName)
    {
        try
        {
            var files = Directory.GetFiles(folderPath, fileName, SearchOption.AllDirectories);
            return files.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private Window? GetMainWindow()
    {
        return (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    }

    [RelayCommand]
    private async Task SelectExportFile()
    {
        var dialog = new SaveFileDialog
        {
            Title = "エクスポートファイルを選択",
            DefaultExtension = "xlsx",
            InitialFileName = $"比較結果_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
            Filters = new List<FileDialogFilter>
            {
                new FileDialogFilter { Name = "Excel Files", Extensions = { "xlsx" } },
                new FileDialogFilter { Name = "All Files", Extensions = { "*" } }
            }
        };

        var window = GetMainWindow();
        if (window != null)
        {
            var result = await dialog.ShowAsync(window);
            if (!string.IsNullOrWhiteSpace(result))
            {
                ExportFilePath = result;
            }
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
