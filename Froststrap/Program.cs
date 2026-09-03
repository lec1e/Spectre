using Avalonia;
using Avalonia.Labs.Notifications;

namespace Froststrap;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);


    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        string iconPath = ExtractToTemp("Spectre.ico", "SpectreNotify.ico");

        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

        if (!OperatingSystem.IsMacOS())
        {
            builder = builder.WithAppNotifications(new AppNotificationOptions
            {
                AppName = "Spectre",
                AppUserModelId = "Eclipse.Eclipse",
                AppIcon = iconPath,
                DisableComServer = true
            });
        }

        return builder;
    }

    public static string ExtractToTemp(string name, string fileName)
    {
        string tempFilePath = Path.Combine(Paths.Temp, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(tempFilePath)!);

        // Always refresh so branding updates aren't stuck on an old Froststrap icon.
        using var stream = Resource.GetStream(name);
        using var fileStream = File.Create(tempFilePath);
        stream.CopyTo(fileStream);

        return tempFilePath;
    }
}