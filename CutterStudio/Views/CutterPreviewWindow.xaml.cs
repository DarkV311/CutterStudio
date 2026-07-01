using System.Windows;

namespace CutterStudio.Views;

public partial class CutterPreviewWindow : Window
{
    public CutterPreviewWindow() => InitializeComponent();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
