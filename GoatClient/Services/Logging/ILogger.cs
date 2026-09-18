using GoatClient.Models;

namespace GoatClient.Services.Logging;

public interface ILogger
{
    void Log(LogLevel level, string message, Exception? exception = null);
}

public static class LoggerExtensions
{
    public static void Debug(this ILogger logger, string message) => logger.Log(LogLevel.Debug, message);

    public static void Info(this ILogger logger, string message) => logger.Log(LogLevel.Info, message);

    public static void Warning(this ILogger logger, string message, Exception? exception = null)
        => logger.Log(LogLevel.Warning, message, exception);

    public static void Error(this ILogger logger, string message, Exception? exception = null)
        => logger.Log(LogLevel.Error, message, exception);

    public static void Critical(this ILogger logger, string message, Exception? exception = null)
        => logger.Log(LogLevel.Critical, message, exception);
}
