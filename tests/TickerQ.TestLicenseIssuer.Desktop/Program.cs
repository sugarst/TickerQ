using AtomUI;
using AtomUI.Desktop.Controls;
using AtomUI.Localization;
using Avalonia;

namespace TickerQ.TestLicenseIssuer.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => AppBuilder.Configure<App>()
        .UseAtomUIPlatformDetect()
        .WithAtomUIDefaultOptions()
        .UseAtomUI(builder => builder
            .UseLanguages(LanguageTags.ZhCN, [LanguageTags.ZhCN, LanguageTags.EnUS])
            .UseDesktopControls())
        .StartWithClassicDesktopLifetime(args);
}
