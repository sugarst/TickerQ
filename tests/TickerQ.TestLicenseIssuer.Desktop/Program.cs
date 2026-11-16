using AtomUI;
using AtomUI.Desktop.Controls;
using Avalonia;

namespace TickerQ.TestLicenseIssuer.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => AppBuilder.Configure<App>()
        .UseAtomUIPlatformDetect()
        .WithAtomUIDefaultOptions()
        .UseAtomUI(builder => builder.UseDesktopControls())
        .StartWithClassicDesktopLifetime(args);
}
