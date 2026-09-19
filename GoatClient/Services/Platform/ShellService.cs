using System.Diagnostics;
using System.IO;
using GoatClient.Services.Logging;

namespace GoatClient.Services.Platform;

public sealed class ShellService : IShellService
{
    private readonly ILogger _logger;

    public ShellService(ILogger logger)
    {
        _logger = logger;
    }

    public bool OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            _logger.Warning($"Folder does not exist: '{path}'.");
            return false;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
            Verb = "open",
        });
        return true;
    }

    public void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Only https links can be opened.", nameof(url));
        }

        _logger.Info($"Opening link: {uri.GetLeftPart(UriPartial.Path)}");
        using var process = Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
    }

    public void RestartApplication()
    {
        _logger.Info("Restarting GOAT CLIENT.");
        ((App)System.Windows.Application.Current).Restart();
    }
}
