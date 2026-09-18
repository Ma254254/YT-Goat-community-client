using GoatClient.Services.Logging;
using Microsoft.Win32;

namespace GoatClient.Services.Platform;

public sealed class StartupRegistrationService : IStartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GoatClient";

    private readonly ILogger _logger;

    public StartupRegistrationService(ILogger logger)
    {
        _logger = logger;
    }

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string;
        }
        catch (Exception ex)
        {
            _logger.Warning("Could not read the Windows startup entry.", ex);
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            var exe = Environment.ProcessPath
                      ?? throw new InvalidOperationException("The launcher executable path could not be determined.");
            key.SetValue(ValueName, $"\"{exe}\"");
            _logger.Info("Windows startup entry created.");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            _logger.Info("Windows startup entry removed.");
        }
    }
}
