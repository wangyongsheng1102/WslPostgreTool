using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
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
    private ObservableCollection<CsvFileInfo> _csvFileInfosOld = new();
    
    [ObservableProperty]
    private ObservableCollection<CsvFileInfo> _csvFileInfosNew = new();

    public int SelectedCsvFileCount => CsvFileInfos.Count(f => f.IsSelected);
    
    public int SelectedCsvFileOldCount => CsvFileInfosOld.Count(f => f.IsSelected);
    
    public int SelectedCsvFileNewCount => CsvFileInfosNew.Count(f => f.IsSelected);

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _exportFilePath = string.Empty;

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
        if (string.IsNullOrEmpty(BaseFolderPath) || (string.IsNullOrWhiteSpace(OldFolderPath) && string.IsNullOrWhiteSpace(NewFolderPath)))
        {
            return;
        }

        try
        {
            var oldFiles = Directory.GetFiles(OldFolderPath, "*.csv", SearchOption.AllDirectories)
                .Select(f => Path.GetFileName(f))
                .ToHashSet();

            var newFiles = Directory.GetFiles(NewFolderPath, "*.csv", SearchOption.AllDirectories)
                .Select(f => Path.GetFileName(f))
                .ToHashSet();

            // 同名ファイルを検出
            var commonFiles = oldFiles.Intersect(newFiles).OrderBy(f => f).ToList();

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
            _mainViewModel.AppendLog($"{CsvFileInfos.Count} 個の同名 CSV ファイルを検出しました。");
        }
        catch (Exception ex)
        {
            _mainViewModel.AppendLog($"[エラー] ファイルリストの取得に失敗しました: {ex.Message}");
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
        if (SelectedConnection == null)
        {
            _mainViewModel.AppendLog("[エラー] データベース接続を選択してください。");
            return;
        }

        if (string.IsNullOrWhiteSpace(OldFolderPath) || string.IsNullOrWhiteSpace(NewFolderPath))
        {
            _mainViewModel.AppendLog("[エラー] 旧フォルダと新フォルダの両方を選択してください。");
            return;
        }

        var selectedFiles = CsvFileInfos.Where(f => f.IsSelected).ToList();
        if (selectedFiles.Count == 0)
        {
            _mainViewModel.AppendLog("[エラー] 比較する CSV ファイルを選択してください。");
            return;
        }

        if (string.IsNullOrWhiteSpace(ExportFilePath))
        {
            _mainViewModel.AppendLog("[エラー] エクスポートファイルパスを指定してください。");
            return;
        }

        try
        {
            IsProcessing = true;
            _mainViewModel.AppendLog($"[処理中] {selectedFiles.Count} 個の CSV ファイルの比較を開始しています...");
            ProgressValue = 0;
            
            await Task.Run(async () => {

                var connectionString = SelectedConnection.GetConnectionString();
                var allResults = new List<RowComparisonResult>();
                int completed = 0;

                foreach (var csvFileInfo in selectedFiles)
                {
                    var csvFileName = csvFileInfo.FileName;
                    _mainViewModel.AppendLog($"[処理中] {csvFileName} を比較しています...");

                    var oldCsvPath = FindCsvFile(OldFolderPath, csvFileName);
                    var newCsvPath = FindCsvFile(NewFolderPath, csvFileName);

                    if (oldCsvPath == null || newCsvPath == null)
                    {
                        _mainViewModel.AppendLog($"[警告] {csvFileName} が見つかりません。スキップします。");
                        continue;
                    }

                    // CSVファイル名からテーブル名を推測（schema.table.csv 形式）
                    var fileNameWithoutExt = Path.GetFileNameWithoutExtension(csvFileName);
                    var parts = fileNameWithoutExt.Split('.');
                    string schemaName, tableName;

                    if (parts.Length >= 2)
                    {
                        schemaName = parts[0];
                        tableName = string.Join(".", parts.Skip(1));
                    }
                    else
                    {
                        schemaName = "public";
                        tableName = fileNameWithoutExt;
                    }

                    // データベースから主キーを取得
                    _mainViewModel.AppendLog($"[処理中] テーブル '{schemaName}.{tableName}' の主キーを取得しています...");
                    List<string> primaryKeys;

                    try
                    {
                        primaryKeys = await _databaseService.GetPrimaryKeyColumnsAsync(
                            connectionString,
                            schemaName,
                            tableName);
                    }
                    catch (Exception ex)
                    {
                        _mainViewModel.AppendLog($"[警告] テーブル '{schemaName}.{tableName}' の主キー取得に失敗しました: {ex.Message}。スキップします。");
                        continue;
                    }

                    if (primaryKeys.Count == 0)
                    {
                        _mainViewModel.AppendLog($"[警告] テーブル '{schemaName}.{tableName}' に主キーがありません。スキップします。");
                        continue;
                    }
                    
                    _mainViewModel.AppendLog($"[情報] 主キー: {string.Join(", ", primaryKeys)}");

                    // CSVファイルを比較
                    var fileProgress = new Progress<(int current, int total, string message)>(p =>
                    {
                        
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            var overallProgress = (completed * 100 + p.current) / selectedFiles.Count;
                            ProgressValue = overallProgress;
                            _mainViewModel.AppendLog($"[{csvFileName}] {p.message}");
                        });
                    });

                    var results = await _csvCompareService.CompareCsvFilesAsync(
                        oldCsvPath,
                        newCsvPath,
                        primaryKeys,
                        connectionString,
                        schemaName,
                        tableName,
                        fileProgress);

                    allResults.AddRange(results);
                    completed++;
                    ProgressValue = (completed * 100) / selectedFiles.Count;
                }

                // Excel にエクスポート
                _mainViewModel.AppendLog("[処理中] Excel ファイルを生成しています...");
                await Task.Run(() =>
                {
                    _excelService.ExportComparisonResults(ExportFilePath, allResults);
                });

                var deletedCount = allResults.Count(r => r.Status == ComparisonStatus.Deleted);
                var addedCount = allResults.Count(r => r.Status == ComparisonStatus.Added);
                var updatedCount = allResults.Count(r => r.Status == ComparisonStatus.Updated);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _mainViewModel.AppendLog($"[完了] 比較が完了しました。");
                    _mainViewModel.AppendLog($"  削除: {deletedCount} 件");
                    _mainViewModel.AppendLog($"  追加: {addedCount} 件");
                    _mainViewModel.AppendLog($"  更新: {updatedCount} 件");
                    _mainViewModel.AppendLog($"  結果を {ExportFilePath} に保存しました。");
                });
            });
        }
        catch (Exception ex)
        {
            _mainViewModel.AppendLog($"[エラー] 比較に失敗しました: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
            ProgressValue = 0;
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
}
