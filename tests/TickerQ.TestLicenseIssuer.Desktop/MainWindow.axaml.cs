using System.Globalization;
using System.Resources;
using AtomUI;
using AtomUI.Desktop.Controls;
using AtomUI.Localization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FixtureExporter = TickerQ.Tests.Licensing.TestLicenseIssuer;

namespace TickerQ.TestLicenseIssuer.Desktop;

public partial class MainWindow : Window
{
    private const string ExpirationFormat = "yyyy-MM-dd HH:mm:ss";
    private static readonly TimeSpan BeijingOffset = TimeSpan.FromHours(8);
    private static readonly Uri PortalUrl = new("https://license.tickerq.net/");
    private static readonly CultureInfo Chinese = CultureInfo.GetCultureInfo("zh-CN");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly ResourceManager Strings = new(
        $"{typeof(MainWindow).Namespace}.DesktopStrings", typeof(MainWindow).Assembly);

    private CultureInfo _culture = Chinese;
    private string? _testExportDirectory;
    private string? _statusKey;
    private object[] _statusArguments = [];

    public MainWindow()
    {
        InitializeComponent();
        LanguagePicker.ItemsSource = new[] { "简体中文", "English" };
        LanguagePicker.SelectedIndex = 0;
        LanguagePicker.SelectionChanged += (_, _) => ApplyLanguage(LanguagePicker.SelectedIndex == 1 ? English : Chinese);
        ApplyLanguage(Chinese);

        var now = DateTimeOffset.UtcNow.ToOffset(BeijingOffset);
        ExpirationPicker.Format = ExpirationFormat;
        ExpirationPicker.MinDate = now.Date;
        ExpirationPicker.SelectedDateTime = now.Date.AddDays(30).AddHours(now.Hour).AddMinutes(now.Minute);
    }

    private void ApplyLanguage(CultureInfo culture)
    {
        _culture = culture;
        Application.Current?.GetLanguageManager()?.ChangeLanguage(culture.Equals(Chinese)
            ? LanguageTags.ZhCN
            : LanguageTags.EnUS);

        Title = Text("WindowTitle");
        PageTitle.Text = Title;
        LanguageLabel.Text = Text("LanguageLabel");
        AutomationProperties.SetName(LanguagePicker, LanguageLabel.Text);
        ServiceHeading.Text = Text("ServiceHeading");
        ServiceDescription.Text = Text("ServiceDescription");
        PortalButton.Content = Text("OpenPortal");
        ServiceExportButton.Content = Text("ExportService");
        TestHeading.Text = Text("TestHeading");
        TestDescription.Text = Text("TestDescription");
        ExpirationLabel.Text = Text("ExpirationLabel");
        AutomationProperties.SetName(ExpirationPicker, ExpirationLabel.Text);
        TestExportButton.Content = Text("ExportTest");
        TestExportTitle.Text = Text("TestExportTitle");
        BeijingResultLabel.Text = Text("BeijingResultLabel");
        UtcResultLabel.Text = Text("UtcResultLabel");
        OpenExportFolderButton.Content = Text("OpenExportFolder");
        OpenExportFolderError.Text = Text("OpenExportFolderError");

        if (_statusKey is not null)
            ExportStatus.Text = Text(_statusKey, _statusArguments);
    }

    private async void OpenPortalAsync(object? sender, RoutedEventArgs args)
    {
        try
        {
            if (!await Launcher.LaunchUriAsync(PortalUrl))
                ShowStatus("PortalFallback", PortalUrl);
        }
        catch (Exception error)
        {
            ShowStatus("PortalFailed", error.Message);
        }
    }

    private async void ExportTestCertificateAsync(object? sender, RoutedEventArgs args)
    {
        if (ExpirationPicker.SelectedDateTime is not { } selected)
        {
            ShowStatus("InvalidExpiration");
            return;
        }

        var beijingExpiration = new DateTimeOffset(DateTime.SpecifyKind(selected, DateTimeKind.Unspecified), BeijingOffset);
        var expiration = beijingExpiration.ToUniversalTime();
        var issuedAt = DateTimeOffset.UtcNow;
        if (expiration <= issuedAt)
        {
            ShowStatus("InvalidExpiration");
            return;
        }

        SetExporting(true);
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrEmpty(appData))
            {
                ShowStatus("DefaultExportDirectoryUnavailable");
                return;
            }

            var directory = Path.Combine(appData, "TickerQ", "TestCertificates");
            var output = await FixtureExporter.ExportAsync(directory, issuedAt, expiration);
            ShowTestExport(output, beijingExpiration, expiration);
        }
        catch (Exception error)
        {
            ShowStatus("ExportFailed", error.Message);
        }
        finally
        {
            SetExporting(false);
        }
    }

    private void ShowTestExport(string directory, DateTimeOffset beijingExpiration, DateTimeOffset utcExpiration)
    {
        _testExportDirectory = directory;
        _statusKey = null;
        BeijingResultValue.Text = beijingExpiration.ToString(ExpirationFormat, CultureInfo.InvariantCulture);
        UtcResultValue.Text = utcExpiration.ToString(ExpirationFormat, CultureInfo.InvariantCulture);
        OpenExportFolderError.IsVisible = false;
        ExportStatusPanel.IsVisible = false;
        TestExportResult.IsVisible = true;
    }

    private async void OpenExportFolderAsync(object? sender, RoutedEventArgs args)
    {
        if (_testExportDirectory is not { } directory)
            return;

        try
        {
            OpenExportFolderError.IsVisible = !await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(directory));
        }
        catch (Exception)
        {
            OpenExportFolderError.IsVisible = true;
        }
    }

    private async void ExportServiceCertificateAsync(object? sender, RoutedEventArgs args)
    {
        SetExporting(true);
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Text("ServiceFileTitle"),
                FileTypeFilter = [new FilePickerFileType(Text("CertificateFileType")) { Patterns = ["*.tqlicense"] }],
                AllowMultiple = false,
            });
            if (files.Count == 0)
                return;

            using var file = files[0];
            var source = file.TryGetLocalPath();
            if (source is null || !source.EndsWith(".tqlicense", StringComparison.OrdinalIgnoreCase))
            {
                ShowStatus("InvalidServiceFile");
                return;
            }

            var directory = await ChooseLocalFolderAsync(Text("ServiceFolderTitle"));
            if (directory is null)
                return;

            var output = Path.Combine(directory, FixtureExporter.CertificateFileName);
            if (File.Exists(output))
            {
                ShowStatus("CertificateExists", FixtureExporter.CertificateFileName);
                return;
            }

            File.Copy(source, output);
            ShowStatus("ServiceExported", output);
        }
        catch (Exception error)
        {
            ShowStatus("ExportFailed", error.Message);
        }
        finally
        {
            SetExporting(false);
        }
    }

    private async Task<string?> ChooseLocalFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        if (folders.Count == 0)
            return null;

        using var folder = folders[0];
        var path = folder.TryGetLocalPath();
        if (path is null)
            ShowStatus("LocalFolderRequired");
        return path;
    }

    private void SetExporting(bool exporting)
    {
        TestExportButton.IsEnabled = !exporting;
        ServiceExportButton.IsEnabled = !exporting;
        PortalButton.IsEnabled = !exporting;
        ExpirationPicker.IsEnabled = !exporting;
        LanguagePicker.IsEnabled = !exporting;
    }

    private string Text(string key, params object[] arguments)
        => string.Format(_culture, Strings.GetString(key, _culture) ?? key, arguments);

    private void ShowStatus(string key, params object[] arguments)
    {
        _statusKey = key;
        _statusArguments = arguments;
        TestExportResult.IsVisible = false;
        ExportStatus.Text = Text(key, arguments);
        ExportStatusPanel.IsVisible = true;
    }
}
