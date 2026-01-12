using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using System.Diagnostics;
using WslPostgreTool.ViewModels;

namespace WslPostgreTool.Views
{
    public partial class AuthorInfoWindow : Window
    {
        public AuthorInfoWindow()
        {
            InitializeComponent();
            
            // ViewModelを設定
            DataContext = new AuthorInfoViewModel();
        }
        
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        
        private void Website_PointerPressed(object sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (DataContext is AuthorInfoViewModel viewModel)
            {
                // ブラウザでウェブサイトを開く
                try
                {
                    var url = viewModel.Website;
                    if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                    {
                        url = "https://" + url;
                    }
                    
                    // プラットフォームごとのブラウザ起動方法
                    if (OperatingSystem.IsWindows())
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                    }
                    else if (OperatingSystem.IsLinux())
                    {
                        Process.Start("xdg-open", url);
                    }
                    else if (OperatingSystem.IsMacOS())
                    {
                        Process.Start("open", url);
                    }
                }
                catch
                {
                    // エラー処理（オプション）
                    // var dialog = new MessageBox("ブラウザを開くことができませんでした。", "エラー");
                    // dialog.ShowDialog(this);
                }
            }
        }
    }
}