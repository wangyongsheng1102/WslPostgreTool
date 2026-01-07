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
using WslPostgreTool.Models;
using WslPostgreTool.Services;

namespace WslPostgreTool.ViewModels;

public partial class CompareViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<DatabaseConnection> _connections = new();

    [ObservableProperty]
    private DatabaseConnection? _oldConnection;

    [ObservableProperty]
    private DatabaseConnection? _newConnection;

    [ObservableProperty]
    private ObservableCollection<TableInfo> _tables = new();

    [ObservableProperty]
    private TableInfo? _selectedTable;

    [ObservableProperty]
    private string _logMessage = string.Empty;

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _exportFilePath = string.Empty;

    private readonly DatabaseService _databaseService = new();
    private readonly CompareService _compareService = new();
    private readonly ExcelExportService _excelService = new();

    public CompareViewModel()
    {
    }

    [RelayCommand]
    private async Task LoadTables()
    {
        if (OldConnection == null || NewConnection == null)
        {
            LogMessage = "[エラー] 旧データベースと新データベースの両方を選択してください。";
            return;
        }

        try
        {
            IsProcessing = true;
            LogMessage = "[処理中] テーブルリストを取得しています...";

            // 旧データベースからテーブルリストを取得
            var oldTables = await _databaseService.GetTablesAsync(OldConnection.GetConnectionString());
            
            // 新データベースからも取得して比較
            var newTables = await _databaseService.GetTablesAsync(NewConnection.GetConnectionString());

            // 両方に存在するテーブルのみを表示
            var commonTables = oldTables
                .Where(ot => newTables.Any(nt => nt.SchemaName == ot.SchemaName && nt.TableName == ot.TableName))
                .ToList();

            Tables.Clear();
            foreach (var table in commonTables)
            {
                Tables.Add(table);
            }

            LogMessage = $"[完了] {Tables.Count} 個の共通テーブルを取得しました。";
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

    [RelayCommand]
    private async Task CompareTables()
    {
        if (OldConnection == null || NewConnection == null)
        {
            LogMessage = "[エラー] 旧データベースと新データベースの両方を選択してください。";
            return;
        }

        if (SelectedTable == null)
        {
            LogMessage = "[エラー] 比較するテーブルを選択してください。";
            return;
        }

        if (string.IsNullOrWhiteSpace(ExportFilePath))
        {
            LogMessage = "[エラー] エクスポートファイルパスを指定してください。";
            return;
        }

        try
        {
            IsProcessing = true;
            LogMessage = "[処理中] データ比較を開始しています...";
            ProgressValue = 0;

            var oldConnectionString = OldConnection.GetConnectionString();
            var newConnectionString = NewConnection.GetConnectionString();

            var results = await _compareService.CompareDatabasesAsync(
                oldConnectionString,
                newConnectionString,
                SelectedTable.SchemaName,
                SelectedTable.TableName,
                new Progress<(int current, int total, string message)>(p =>
                {
                    ProgressValue = p.current;
                    LogMessage = p.message;
                }));

            // Excel にエクスポート
            LogMessage = "[処理中] Excel ファイルを生成しています...";
            _excelService.ExportComparisonResults(ExportFilePath, results);

            var deletedCount = results.Count(r => r.Status == ComparisonStatus.Deleted);
            var addedCount = results.Count(r => r.Status == ComparisonStatus.Added);
            var updatedCount = results.Count(r => r.Status == ComparisonStatus.Updated);

            LogMessage = $"[完了] 比較が完了しました。削除: {deletedCount}, 追加: {addedCount}, 更新: {updatedCount}。結果を {ExportFilePath} に保存しました。";
        }
        catch (Exception ex)
        {
            LogMessage = $"[エラー] 比較に失敗しました: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            ProgressValue = 0;
        }
    }
}

