using System;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WslPostgreTool.ViewModels
{
    public partial class AuthorInfoViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _authorName = "wangys";
        
        [ObservableProperty]
        private string _authorTitle = "打ショウユのやつ";
        
        [ObservableProperty]
        private string _company = "信华信技术股份有限公司";
        
        [ObservableProperty]
        private string _location = "中国, 大連";
        
        [ObservableProperty]
        private string _email = "yongsheng.wang@dhc.com.cn";
        
        [ObservableProperty]
        private string _website = "https://github.com/wangyongsheng1102";
        
        [ObservableProperty]
        private ObservableCollection<SkillInfo> _skills;
        
        [ObservableProperty]
        private ObservableCollection<ContributionInfo> _contributions;
        
        [ObservableProperty]
        private string _acknowledgements = "ラララ" + 
                                           Environment.NewLine + Environment.NewLine +
                                           "シシシ";
        
        [ObservableProperty]
        private DateTime _lastUpdated = new DateTime(2026, 01, 12);
        
        // 添加格式化后的日期字符串
        public string LastUpdatedFormatted => LastUpdated.ToString("yyyy年MM月dd日", CultureInfo.GetCultureInfo("ja-JP"));
        
        [ObservableProperty]
        private string _copyright = "© 2026 wangys. All rights reserved.";
        
        public AuthorInfoViewModel()
        {
            InitializeSkills();
            InitializeContributions();
        }
        
        private void InitializeSkills()
        {
            Skills = new ObservableCollection<SkillInfo>
            {
                new SkillInfo
                {
                    Icon = "⚙️",
                    Name = "PostgreSQL データベース開発",
                    Description = "ストアドプロシージャ、トリガー、パフォーマンスチューニング、レプリケーション設定",
                    Proficiency = 95
                },
                new SkillInfo
                {
                    Icon = "💻",
                    Name = "C# / .NET 開発",
                    Description = "Avalonia UI, WPF, ASP.NET Core, Entity Framework Core",
                    Proficiency = 90
                },
                new SkillInfo
                {
                    Icon = "🎨",
                    Name = "UI/UX デザイン",
                    Description = "ユーザーインターフェース設計、ユーザビリティテスト、レスポンシブデザイン",
                    Proficiency = 85
                },
                new SkillInfo
                {
                    Icon = "🔗",
                    Name = "API 開発 & 統合",
                    Description = "RESTful API, gRPC, WebSocket, サードパーティサービス連携",
                    Proficiency = 88
                },
                new SkillInfo
                {
                    Icon = "🧪",
                    Name = "テスト & 品質保証",
                    Description = "単体テスト、統合テスト、E2Eテスト、CI/CDパイプライン構築",
                    Proficiency = 92
                },
                new SkillInfo
                {
                    Icon = "📊",
                    Name = "データ分析 & 可視化",
                    Description = "SQL分析、パフォーマンスモニタリング、レポート生成",
                    Proficiency = 87
                }
            };
        }
        
        private void InitializeContributions()
        {
            Contributions = new ObservableCollection<ContributionInfo>
            {
                new ContributionInfo
                {
                    Icon = "🎯",
                    TypeColor = Brushes.DarkGreen,
                    Description = "PostgreSQL専用データ比較エンジンの設計と実装",
                    Date = new DateTime(2024, 10, 20),
                    Status = "完了",
                    StatusColor = Brushes.SeaGreen
                },
                new ContributionInfo
                {
                    Icon = "⚡",
                    TypeColor = Brushes.DarkOrange,
                    Description = "並列処理によるデータインポート/エクスポートの最適化",
                    Date = new DateTime(2024, 9, 15),
                    Status = "完了",
                    StatusColor = Brushes.SeaGreen
                },
                new ContributionInfo
                {
                    Icon = "🔒",
                    TypeColor = Brushes.Purple,
                    Description = "セキュリティ強化とデータ暗号化機能の追加",
                    Date = new DateTime(2024, 8, 30),
                    Status = "完了",
                    StatusColor = Brushes.SeaGreen
                },
                new ContributionInfo
                {
                    Icon = "🌐",
                    TypeColor = Brushes.DarkBlue,
                    Description = "多言語対応（日本語/英語）の実装",
                    Date = new DateTime(2024, 7, 25),
                    Status = "完了",
                    StatusColor = Brushes.SeaGreen
                },
                new ContributionInfo
                {
                    Icon = "📈",
                    TypeColor = Brushes.Teal,
                    Description = "パフォーマンスモニタリングダッシュボードの開発",
                    Date = new DateTime(2024, 11, 5),
                    Status = "進行中",
                    StatusColor = Brushes.DodgerBlue
                },
                new ContributionInfo
                {
                    Icon = "🤖",
                    TypeColor = Brushes.Indigo,
                    Description = "AI支援によるクエリ最適化機能の研究開発",
                    Date = new DateTime(2024, 10, 10),
                    Status = "計画中",
                    StatusColor = Brushes.Gray
                }
            };
        }
    }
    
    public partial class SkillInfo : ObservableObject
    {
        [ObservableProperty]
        private string _icon = string.Empty;
        
        [ObservableProperty]
        private string _name = string.Empty;
        
        [ObservableProperty]
        private string _description = string.Empty;
        
        [ObservableProperty]
        private int _proficiency;
    }
    
    public partial class ContributionInfo : ObservableObject
    {
        [ObservableProperty]
        private string _icon = string.Empty;
        
        [ObservableProperty]
        private IBrush? _typeColor;
        
        [ObservableProperty]
        private string _description = string.Empty;
        
        [ObservableProperty]
        private DateTime _date;
        
        [ObservableProperty]
        private string _status = string.Empty;
        
        [ObservableProperty]
        private IBrush? _statusColor;
    }
}