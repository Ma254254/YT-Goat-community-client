using System.ComponentModel;
using GoatClient.Models;
using GoatClient.Services.Minecraft;

namespace GoatClient.Services.Launch;

public interface IMinecraftLauncherService : INotifyPropertyChanged
{
    MinecraftProcessState ProcessState { get; }

    bool IsRunning { get; }

    int? LastExitCode { get; }

    /// <summary>Launch log lines (tokens redacted). Raised on a background thread.</summary>
    event EventHandler<string>? LogLine;

    /// <summary>Raised when the Minecraft process ends. Raised on a background thread.</summary>
    event EventHandler<int>? ProcessExited;

    /// <summary>Path of the current launch log file.</summary>
    string? CurrentLogFile { get; }

    /// <summary>Builds the command line from the official version JSON and starts the real process.</summary>
    Task LaunchAsync(LauncherProfile profile, VersionManifest version, JavaRuntime runtime, MinecraftSession session, CancellationToken cancellationToken);
}

/// <summary>Launch failure with a user-readable reason and a suggested fix.</summary>
public sealed class LaunchFailedException : Exception
{
    public LaunchFailedException(string message, LaunchFix fix, Exception? inner = null)
        : base(message, inner)
    {
        Fix = fix;
    }

    public LaunchFix Fix { get; }
}

public enum LaunchFix
{
    None,
    RepairInstallation,
    InstallJava,
    SignIn,
}
