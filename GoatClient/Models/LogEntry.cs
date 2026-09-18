namespace GoatClient.Models;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Critical,
}

public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message, string? Exception)
{
    public string Format()
    {
        var line = $"{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level.ToString().ToUpperInvariant(),-8}] {Message}";
        return Exception is null ? line : line + Environment.NewLine + Exception;
    }
}
