using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Reflection;
using CutterStudio.Controls;
using CutterStudio.ViewModels;

namespace CutterStudio.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Title = $"Cutter Studio v{AppVersion()}";
        Loaded += MainWindow_Loaded;
        Closing += (_, _) => _viewModel.PersistUserSettings();
    }

    private static string AppVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? "0.1";

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Initialization error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void FitButton_Click(object sender, RoutedEventArgs e) => PreviewCanvas.FitToScreen();
    private void ZoomInButton_Click(object sender, RoutedEventArgs e) => PreviewCanvas.ZoomBy(1.2);
    private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => PreviewCanvas.ZoomBy(1 / 1.2);

    private void CutterPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.PreviewArtwork is null || _viewModel.LayoutMetrics is null)
        {
            MessageBox.Show("Import artwork first.", "Vinyl Preview",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        new CutterPreviewWindow
        {
            Owner = this,
            DataContext = _viewModel
        }.ShowDialog();
    }

    private void PreviewCanvas_ZoomChanged(object? sender, EventArgs e) =>
        ZoomText.Text = $"{PreviewCanvas.Zoom * 100:0}%";

    private void PreviewCanvas_ArtworkMoved(object? sender, ArtworkMovedEventArgs e) =>
        _viewModel.MoveArtwork(e.DeltaXmm, e.DeltaYmm);

    private void RecentProjects_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.OpenCommand.CanExecute(null))
            _viewModel.OpenCommand.Execute(null);
    }

    private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsDigit);

    private void NumericTextBox_OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            e.CancelCommand();
            return;
        }

        var text = e.DataObject.GetData(DataFormats.UnicodeText) as string;
        if (string.IsNullOrWhiteSpace(text) || !text.All(char.IsDigit))
            e.CancelCommand();
    }
}
