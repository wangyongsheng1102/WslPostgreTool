using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using WslPostgreTool.Models;
using WslPostgreTool.ViewModels;

namespace WslPostgreTool.Views;

public partial class MainView : Window
{
    
    private bool _shouldAutoScroll = true;
    private VersionHistoryWindow? _versionHistoryWindow;
    private AuthorInfoWindow? _authorInfoWindow;
    
    public MainView()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        
        // 设置 DataContext 后，初始化日志滚动逻辑
        if (DataContext is MainViewModel viewModel)
        {
            // 监听 LogMessages 集合变化
            viewModel.LogMessages.CollectionChanged += (s, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add && _shouldAutoScroll)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (LogScrollViewer != null)
                        {
                            LogScrollViewer.ScrollToEnd();
                        }
                    }, DispatcherPriority.Background);
                }
            };
            
            // 订阅 RequestScrollToBottom 事件（作为备用机制）
            viewModel.RequestScrollToBottom += () =>
            {
                if (_shouldAutoScroll && LogScrollViewer != null)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        LogScrollViewer.ScrollToEnd();
                    }, DispatcherPriority.Background);
                }
            };
        }
        
        // 监听用户滚动
        if (this.FindControl<ScrollViewer>("LogScrollViewer") is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollChanged += (s, e) =>
            {
                // 检测是否接近底部（允许 5 像素的误差）
                var scrollOffset = scrollViewer.Offset.Y;
                var maxScroll = scrollViewer.Extent.Height - scrollViewer.Viewport.Height;
                var isAtBottom = scrollOffset >= maxScroll - 5;
                
                // 如果用户滚动到底部，恢复自动滚动
                if (isAtBottom)
                {
                    _shouldAutoScroll = true;
                }
                // 如果用户向上滚动，暂停自动滚动
                else if (scrollOffset < maxScroll - 10)
                {
                    _shouldAutoScroll = false;
                }
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
        // 如果窗口已经打开，则显示它；否则创建新窗口
        if (_versionHistoryWindow != null)
        {
            try
            {
                _versionHistoryWindow.Activate();
                _versionHistoryWindow.BringIntoView();
            }
            catch
            {
                // 如果窗口已关闭，创建新窗口
                _versionHistoryWindow = new VersionHistoryWindow();
                _versionHistoryWindow.Closed += (s, args) =>
                {
                    _versionHistoryWindow = null;
                };
                _versionHistoryWindow.Show();
            }
        }
        else
        {
            _versionHistoryWindow = new VersionHistoryWindow();
            _versionHistoryWindow.Closed += (s, args) =>
            {
                _versionHistoryWindow = null;
            };
            _versionHistoryWindow.Show();
        }
    }
    
    private void AuthorTextBlock_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        // 如果窗口已经打开，则显示它；否则创建新窗口
        if (_authorInfoWindow != null)
        {
            try
            {
                _authorInfoWindow.Activate();
                _authorInfoWindow.BringIntoView();
            }
            catch
            {
                // 如果窗口已关闭，创建新窗口
                _authorInfoWindow = new AuthorInfoWindow();
                _authorInfoWindow.Closed += (s, args) =>
                {
                    _authorInfoWindow = null;
                };
                _authorInfoWindow.Show();
            }
        }
        else
        {
            _authorInfoWindow = new AuthorInfoWindow();
            _authorInfoWindow.Closed += (s, args) =>
            {
                _authorInfoWindow = null;
            };
            _authorInfoWindow.Show();
        }
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

