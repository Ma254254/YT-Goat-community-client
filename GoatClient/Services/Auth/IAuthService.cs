using System.ComponentModel;
using GoatClient.Models;

namespace GoatClient.Services.Auth;

public interface IAuthService : INotifyPropertyChanged
{
    MinecraftAccount? Account { get; }

    AuthState State { get; }

    /// <summary>True when a Microsoft application (client) ID is configured.</summary>
    bool IsConfigured { get; }

    /// <summary>Loads the cached profile and tries a silent token refresh.</summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>Step 1 of sign-in: requests a device code the user enters at microsoft.com/link.</summary>
    Task<DeviceCodeInfo> StartDeviceLoginAsync(CancellationToken cancellationToken);

    /// <summary>Step 2: waits for the user to approve, then completes Xbox → XSTS → Minecraft.</summary>
    Task CompleteDeviceLoginAsync(DeviceCodeInfo code, CancellationToken cancellationToken);

    /// <summary>Returns a valid Minecraft session, refreshing tokens if necessary.</summary>
    Task<MinecraftSession> GetSessionAsync(CancellationToken cancellationToken);

    Task SignOutAsync();
}

/// <summary>Authentication failure with a user-readable message.</summary>
public sealed class AuthException : Exception
{
    public AuthException(string message, bool requiresSignIn, Exception? inner = null)
        : base(message, inner)
    {
        RequiresSignIn = requiresSignIn;
    }

    public bool RequiresSignIn { get; }
}
