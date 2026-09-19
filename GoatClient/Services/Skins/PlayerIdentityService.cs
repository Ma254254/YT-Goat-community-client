using System.ComponentModel;
using System.IO;
using GoatClient.Core;
using GoatClient.Core.Constants;
using GoatClient.Core.Helpers;
using GoatClient.Models;
using GoatClient.Services.Auth;
using GoatClient.Services.Logging;

namespace GoatClient.Services.Skins;

public enum IdentitySource
{
    None,

    /// <summary>Public profile linked by name (official launcher mode, no login).</summary>
    LinkedName,

    /// <summary>Signed in inside GOAT CLIENT (direct launch).</summary>
    SignedIn,
}

/// <summary>
/// "Who is the player?" for the UI (name, UUID, skin): the signed-in account if there is one,
/// otherwise the public profile linked by Minecraft name. Never touches the official launcher's files.
/// </summary>
public sealed class PlayerIdentityService : ObservableObject
{
    private static string CacheFile => Path.Combine(AppPaths.Config, "linked-profile.json");

    private readonly IAuthService _auth;
    private readonly MojangProfileLookup _lookup;
    private readonly JsonFileStore _store;
    private readonly ILogger _logger;
    private MinecraftAccount? _linked;

    public PlayerIdentityService(IAuthService auth, MojangProfileLookup lookup, JsonFileStore store, ILogger logger)
    {
        _auth = auth;
        _lookup = lookup;
        _store = store;
        _logger = logger;
        _auth.PropertyChanged += OnAuthChanged;
    }

    public MinecraftAccount? Current => _auth.State == AuthState.SignedIn && _auth.Account is not null ? _auth.Account : _linked;

    public MinecraftAccount? LinkedProfile => _linked;

    public IdentitySource Source => _auth.State == AuthState.SignedIn && _auth.Account is not null
        ? IdentitySource.SignedIn
        : _linked is not null ? IdentitySource.LinkedName : IdentitySource.None;

    /// <summary>Loads the cached linked profile, then refreshes name/skin from Mojang in the background.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _linked = await _store.LoadAsync<MinecraftAccount>(CacheFile, cancellationToken);
        RaiseChanged();
        if (_linked is null)
        {
            return;
        }

        try
        {
            var fresh = await _lookup.GetByUuidAsync(_linked.Uuid, cancellationToken);
            if (fresh is not null)
            {
                await SetLinkedAsync(fresh, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning("Linked Minecraft profile could not be refreshed (offline?) – using the cached copy.", ex);
        }
    }

    /// <summary>Links a public profile by name. Returns false if no such Minecraft account exists.</summary>
    public async Task<bool> LinkAsync(string name, CancellationToken cancellationToken)
    {
        var profile = await _lookup.FindByNameAsync(name.Trim(), cancellationToken);
        if (profile is null)
        {
            return false;
        }

        await SetLinkedAsync(profile, cancellationToken);
        _logger.Info($"Minecraft name linked: {profile.Username}.");
        return true;
    }

    public async Task UnlinkAsync()
    {
        _linked = null;
        try
        {
            if (File.Exists(CacheFile))
            {
                File.Delete(CacheFile);
            }
        }
        catch (IOException ex)
        {
            _logger.Warning("Could not delete the linked profile cache.", ex);
        }

        RaiseChanged();
        await Task.CompletedTask;
    }

    /// <summary>Re-reads the public profile (e.g. after the skin was changed on minecraft.net).</summary>
    public async Task RefreshLinkedAsync(CancellationToken cancellationToken)
    {
        if (_linked is not null && await _lookup.GetByUuidAsync(_linked.Uuid, cancellationToken) is { } fresh)
        {
            await SetLinkedAsync(fresh, cancellationToken);
        }
    }

    private async Task SetLinkedAsync(MinecraftAccount profile, CancellationToken cancellationToken)
    {
        _linked = profile;
        await _store.SaveAsync(CacheFile, profile, cancellationToken);
        RaiseChanged();
    }

    private void OnAuthChanged(object? sender, PropertyChangedEventArgs e) => RaiseChanged();

    private void RaiseChanged()
    {
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(LinkedProfile));
        OnPropertyChanged(nameof(Source));
    }
}
