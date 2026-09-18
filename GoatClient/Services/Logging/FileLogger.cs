using System.IO;
using System.Threading.Channels;
using GoatClient.Models;

namespace GoatClient.Services.Logging;

/// <summary>
/// Writes log entries to %APPDATA%\GoatClient\logs\goatclient-yyyy-MM-dd.log.
/// Entries are queued and written on a background task, so logging never blocks the UI thread.
/// </summary>
public sealed class FileLogger : ILogger, IDisposable
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(14);

    private readonly Channel<LogEntry> _channel = Channel.CreateUnbounded<LogEntry>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly string _directory;
    private readonly Task _writerTask;
    private bool _disposed;

    public FileLogger(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        _writerTask = Task.Run(WriteLoopAsync);
        CleanupOldLogs();
    }

    public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    public string CurrentLogFile => Path.Combine(_directory, $"goatclient-{DateTime.Now:yyyy-MM-dd}.log");

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (level < MinimumLevel)
        {
            return;
        }

        var entry = new LogEntry(DateTimeOffset.Now, level, message, exception?.ToString());
        System.Diagnostics.Debug.WriteLine(entry.Format());
        _channel.Writer.TryWrite(entry);
    }

    private async Task WriteLoopAsync()
    {
        var reader = _channel.Reader;
        try
        {
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                try
                {
                    await using var stream = new FileStream(CurrentLogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    await using var writer = new StreamWriter(stream);
                    while (reader.TryRead(out var entry))
                    {
                        await writer.WriteLineAsync(entry.Format()).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    System.Diagnostics.Debug.WriteLine($"[GoatClient] Log write failed: {ex}");
                    await Task.Delay(250).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GoatClient] Logger stopped: {ex}");
        }
    }

    private void CleanupOldLogs()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_directory, "goatclient-*.log"))
            {
                if (DateTime.Now - File.GetLastWriteTime(file) > Retention)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Warning, "Could not clean up old log files.", ex);
        }
    }

    /// <summary>Flushes pending entries (max. 3 seconds) and stops the writer.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.TryComplete();
        try
        {
            _writerTask.Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GoatClient] Logger dispose failed: {ex}");
        }
    }
}
