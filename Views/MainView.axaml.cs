using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using WslPostgreTool.Models;
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
    
    private void OnItemPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is CsvFileInfo fileInfo)
        {
            // 切换选中状态
            fileInfo.IsSelected = !fileInfo.IsSelected;
            e.Handled = true;
        }
        if (sender is Border border1 && border1.DataContext is TableInfo fileInfo1)
        {
            // 切换选中状态
            fileInfo1.IsSelected = !fileInfo1.IsSelected;
            e.Handled = true;
        }
    }
    
    private void VersionTextBlock_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        var versionWindow = new VersionHistoryWindow();
        versionWindow.Show();
    }
    private void AuthorTextBlock_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        var authorInfoWindow = new AuthorInfoWindow();
        authorInfoWindow.Show();
    }
    
    private void ConnectionsCount_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        // 跳转到第一个 TabItem（接続設定）
        if (MainTabControl != null && MainTabControl.Items != null && MainTabControl.Items.Count > 0)
        {
            // 方法1：通过索引选择
            MainTabControl.SelectedIndex = 0;
        
            // 方法2：通过具体的 TabItem 选择
            MainTabControl.SelectedItem = ConnectionTab;
        }
    
        e.Handled = true;
    }
}

