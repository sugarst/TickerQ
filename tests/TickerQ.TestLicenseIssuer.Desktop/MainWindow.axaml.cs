using AtomUI.Desktop.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FixtureExporter = TickerQ.Tests.Licensing.TestLicenseIssuer;

namespace TickerQ.TestLicenseIssuer.Desktop;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private async void ExportAsync(object? sender, RoutedEventArgs args)
    {
        ExportButton.IsEnabled = false;
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose where to export the test fixture",
                AllowMultiple = false,
            });
            if (folders.Count == 0)
                return;

            using var folder = folders[0];
            var parentDirectory = folder.TryGetLocalPath();
            if (parentDirectory is null)
            {
                ShowStatus("Choose a folder on a local drive.");
                return;
            }

            var output = await FixtureExporter.ExportAsync(parentDirectory, DateTimeOffset.UtcNow);
            ShowStatus($"Exported tickerq.tqlicense and fixture-authority.json to\n{output}");
        }
        catch (Exception error)
        {
            ShowStatus($"Export failed. {error.Message}");
        }
        finally
        {
            ExportButton.IsEnabled = true;
        }
    }

    private void ShowStatus(string message)
    {
        ExportStatus.Text = message;
        ExportStatus.IsVisible = true;
    }
}
