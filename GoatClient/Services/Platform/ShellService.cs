using System.Diagnostics;
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
}
