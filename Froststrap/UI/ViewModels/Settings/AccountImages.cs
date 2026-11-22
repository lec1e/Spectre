using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Froststrap.Integrations;
using Froststrap.UI.Elements.Settings;

namespace Froststrap.UI.ViewModels.Settings;

internal static class AccountImages
{
    public static async Task<Bitmap?> LoadAsync(AccountStudioClient client, string url)
    {
        try
        {
            var bytes = await client.DownloadBytesAsync(url, CancellationToken.None);
            return new Bitmap(new MemoryStream(bytes));
        }
        catch (Exception ex) when (ex is HttpRequestException or RobloxApiException or InvalidOperationException)
        {
            return null;
        }
    }

    public static async Task SaveAsync(DownloadedFile file)
    {
        var saved = await PickAsync(file.FileName);
        if (saved is null)
            return;

        await using var stream = await saved.OpenWriteAsync();
        await stream.WriteAsync(file.Bytes);
    }

    public static async Task SaveManyAsync(string folderName, IReadOnlyList<DownloadedFile> files)
    {
        if (files.Count == 0)
            return;

        if (files.Count == 1)
        {
            await SaveAsync(files[0]);
            return;
        }

        var first = await PickAsync(files[0].FileName);
        if (first is null)
            return;

        var folder = Path.GetDirectoryName(first.Path.LocalPath);
        if (string.IsNullOrEmpty(folder))
            return;

        Directory.CreateDirectory(folder);
        foreach (var file in files)
            await File.WriteAllBytesAsync(Path.Combine(folder, file.FileName), file.Bytes);
    }

    private static async Task<IStorageFile?> PickAsync(string suggestedName)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(async () => await PickAsync(suggestedName));

        var window = FindWindow();
        if (window is null)
            throw new RobloxApiException("Spectre's window is not open.");

        IStorageFolder? downloads = null;
        try
        {
            downloads = await window.StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Downloads);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
        }

        return await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Roblox asset",
            SuggestedFileName = suggestedName,
            SuggestedStartLocation = downloads
        });
    }

    private static Window? FindWindow()
    {
        if (MainWindow.Instance is { IsVisible: true } settings)
            return settings;

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        return desktop.MainWindow
            ?? desktop.Windows.FirstOrDefault(window => window.IsVisible);
    }
}
