using System;
using Avalonia;

namespace FreshViewer;

/// <summary>
/// Entry point hosting Avalonia's desktop lifetime for FreshViewer.
/// </summary>
internal static class Program
{
    [STAThread]
    /// <summary>
    /// Validates platform requirements and starts the Avalonia application.
    /// </summary>
    public static void Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("FreshViewer is now available on Windows only.");
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Creates and configures the Avalonia application builder.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseWin32()
            .UseSkia()
            .WithInterFont()
            .LogToTrace();
}
