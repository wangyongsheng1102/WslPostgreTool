using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WslPostgreTool.ViewModels;

namespace WslPostgreTool.ViewModels
{
    public partial class VersionHistoryViewModel : ViewModelBase
    {
        [ObservableProperty]
        private ObservableCollection<VersionInfo> _versionHistory;
        
        [ObservableProperty]
        private string _currentVersionInfo;
        
        public VersionHistoryViewModel()
        {
            InitializeVersionHistory();
        }
        
        private void InitializeVersionHistory()
        {
            VersionHistory = new ObservableCollection<VersionInfo>
            {
                // new VersionInfo
                // {
                //     Version = "2.1.0",
                //     Title = "パフォーマンス最適化と新機能追加",
                //     ReleaseDate = new DateTime(2024, 11, 15),
                //     Description = "• データ比較処理の速度を最大40%向上\n• PostgreSQL 16 の正式サポート追加\n• エクスポート時のCSV/JSON形式オプション追加\n• バッチ処理機能の改善とエラーハンドリング強化\n• UIのレスポンシブデザインを改善",
                //     UpdateType = "メジャーアップデート",
                //     UpdateTypeColor = Brushes.DarkGreen
                // },
                // new VersionInfo
                // {
                //     Version = "2.0.3",
                //     Title = "バグ修正と安定性向上",
                //     ReleaseDate = new DateTime(2024, 9, 28),
                //     Description = "• 接続プールのメモリリーク問題を修正\n• 大量データインポート時のタイムアウト問題を解決\n• 日本語ロケールでの日時フォーマット問題を修正\n• ログ出力の詳細化とパフォーマンス改善",
                //     UpdateType = "バグ修正",
                //     UpdateTypeColor = Brushes.OrangeRed
                // },
                // new VersionInfo
                // {
                //     Version = "2.0.0",
                //     Title = "PostgreSQL専用版リリース",
                //     ReleaseDate = new DateTime(2024, 8, 10),
                //     Description = "• PostgreSQL専用機能の全面実装\n• 新しい差分比較アルゴリズム搭載\n• 並列処理による高速化実現\n• カスタムクエリビルダーの追加\n• レポート自動生成機能の実装",
                //     UpdateType = "メジャーリリース",
                //     UpdateTypeColor = Brushes.DarkBlue
                // },
                // new VersionInfo
                // {
                //     Version = "1.5.2",
                //     Title = "セキュリティ強化アップデート",
                //     ReleaseDate = new DateTime(2024, 6, 20),
                //     Description = "• SSL/TLS接続の強化\n• パスワード暗号化方式の改善\n• 監査ログ機能の追加\n• セッション管理の強化\n• 脆弱性スキャン対応",
                //     UpdateType = "セキュリティ",
                //     UpdateTypeColor = Brushes.Purple
                // },
                // new VersionInfo
                // {
                //     Version = "1.4.0",
                //     Title = "UI改善と操作性向上",
                //     ReleaseDate = new DateTime(2024, 4, 15),
                //     Description = "• ダークモードの実装\n• ショートカットキーの充実\n• ドラッグ＆ドロップ対応\n• 設定のエクスポート/インポート機能\n• 多言語対応の改善",
                //     UpdateType = "機能改善",
                //     UpdateTypeColor = Brushes.Teal
                // }
                new VersionInfo
                {
                    Version = "1.0.0",
                    Title = "新版作成",
                    ReleaseDate = new DateTime(2026, 1, 12),
                    Description = "• 機能新規作成\n• 基本的なUI設計\n• 初期リリースバージョン",
                    UpdateType = "新版作成",
                    UpdateTypeColor = Brushes.BlueViolet
                }
            };
            
            // 最新バージョン情報を設定
            var latestVersion = VersionHistory.First();
            CurrentVersionInfo = $"現在のバージョン: {latestVersion.Version} (最終更新: {latestVersion.ReleaseDate:yyyy/MM/dd})";
        }
    }
    
    public partial class VersionInfo : ObservableObject
    {
        [ObservableProperty]
        private string _version;
        
        [ObservableProperty]
        private string _title;
        
        [ObservableProperty]
        private DateTime _releaseDate;
        
        [ObservableProperty]
        private string _description;
        
        [ObservableProperty]
        private string _updateType;
        
        [ObservableProperty]
        private IBrush _updateTypeColor;
    }
}