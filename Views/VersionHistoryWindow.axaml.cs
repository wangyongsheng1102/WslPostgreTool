using Avalonia.Controls;
using Avalonia.Interactivity;
using WslPostgreTool.ViewModels;

namespace WslPostgreTool.Views
{
    public partial class VersionHistoryWindow : Window
    {
        public VersionHistoryWindow()
        {
            InitializeComponent();
            
            // ViewModelを設定
            DataContext = new VersionHistoryViewModel();
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}