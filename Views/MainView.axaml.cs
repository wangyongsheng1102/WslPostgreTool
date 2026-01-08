using System;
using Avalonia.Controls;
using Avalonia.Threading;
using WslPostgreTool.ViewModels;

namespace WslPostgreTool.Views;

public partial class MainView : Window
{
    
    private bool _shouldAutoScroll = true;
    
    public MainView()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        
        
        // TODO 优化改进可
        // 监听 ItemsControl 的布局更新
        if (this.FindControl<Grid>("LogGrid") is Grid grid)
        {
            grid.LayoutUpdated += (s, e) =>
            {
                if (!_shouldAutoScroll) return;
                
                if (this.FindControl<ScrollViewer>("LogScrollViewer") is ScrollViewer scrollViewer)
                {
                    scrollViewer.ScrollToEnd();
                }
            };
        }
        
        
        // 监听用户滚动
        if (this.FindControl<ScrollViewer>("LogScrollViewer") is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollChanged += (s, e) =>
            {
                // 当用户手动向上滚动时，停止自动滚动
                var isAtBottom = scrollViewer.Offset.Y >= 
                                 scrollViewer.Extent.Height - scrollViewer.Viewport.Height - 1;
                _shouldAutoScroll = isAtBottom;
            };
        }
    }
}

