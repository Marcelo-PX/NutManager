using Avalonia;
using NutManager.Infrastructure.Platform.Windows;

namespace NutManager.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (WindowsElevatedHelper.TryHandle(args, out var exitCode))
        {
            Environment.ExitCode = exitCode;
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
