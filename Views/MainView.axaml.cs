using Avalonia.Controls;
using WslPostgreTool.ViewModels;

namespace WslPostgreTool.Views;

public partial class MainView : Window
{
    public MainView()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}

