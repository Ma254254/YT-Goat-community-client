using System.IO;
using GoatClient.Core.Constants;
using GoatClient.Core.Helpers;
using GoatClient.Models;
using GoatClient.Services.Logging;
using GoatClient.Services.Platform;
using GoatClient.Services.Settings;
using GoatClient.Services.Status;

namespace GoatClient.Services.Profiles;

/// <summary>Persists profiles in %APPDATA%\GoatClient\profiles.json.</summary>
public sealed class ProfileService : IProfileService
{
    public const int MaxNameLength = 32;

    private readonly JsonFileStore _store;
    private readonly ISettingsService _settings;
    private readonly ISystemInfoService _systemInfo;
    private readonly IStatusService _status;
    private readonly ILogger _logger;

    private List<LauncherProfile> _profiles = new();
    private Guid? _selectedId;

    public ProfileService(
        JsonFileStore store,
        ISettingsService settings,
        ISystemInfoService systemInfo,
        IStatusService status,
        ILogger logger)
    {
        _store = store;
        _settings = settings;
        _systemInfo = systemInfo;
        _status = status;
        _logger = logger;
    }

    public IReadOnlyList<LauncherProfile> Profiles => _profiles;

    public LauncherProfile? SelectedProfile => _profiles.FirstOrDefault(p => p.Id == _selectedId) ?? _profiles.FirstOrDefault();

    public event EventHandler? ProfilesChanged;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var data = await _store.LoadAsync<ProfileStoreData>(AppPaths.ProfilesFile, cancellationToken);
        var dirty = data is null;

        var profiles = new List<LauncherProfile>();
        foreach (var profile in data?.Profiles ?? new List<LauncherProfile>())
        {
            if (profile is null)
            {
                dirty = true;
                continue;
            }

            if (profile.Id == Guid.Empty || profiles.Any(p => p.Id == profile.Id))
            {
                profile.Id = Guid.NewGuid();
                dirty = true;
            }

            dirty |= Normalize(profile);
            profiles.Add(profile);
        }

        if (profiles.Count == 0)
        {
            profiles.Add(CreateDefaultProfile());
            dirty = true;
            _logger.Info("No profiles found – created the default profile.");
        }

        _profiles = profiles;

        // Startup selection: configured default profile → last selected → first.
        var preferred = _settings.Current.DefaultProfileId;
        _selectedId = preferred is not null && _profiles.Any(p => p.Id == preferred)
            ? preferred
            : data?.SelectedProfileId is { } last && _profiles.Any(p => p.Id == last)
                ? last
                : _profiles[0].Id;

        if (dirty)
        {
            await PersistAsync(cancellationToken);
        }

        _logger.Info($"{_profiles.Count} profile(s) loaded.");
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task SaveAsync(CancellationToken cancellationToken = default) => PersistAsync(cancellationToken);

    public LauncherProfile CreateDraft()
    {
        var settings = _settings.Current;
        var name = UniqueName("New Profile");
        return new LauncherProfile
        {
            Name = name,
            MinecraftVersion = settings.DefaultVersion,
            RamMb = _systemInfo.ClampRam(settings.DefaultRamMb),
            JavaPreference = JavaRuntimePreference.Automatic,
            GameDirectory = CreateInstanceDirectoryPath(name, settings.DefaultVersion),
            JvmArguments = settings.DefaultJvmArguments,
        };
    }

    public string? Validate(LauncherProfile profile)
    {
        var name = profile.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return "Please enter a profile name.";
        }

        if (name.Length > MaxNameLength)
        {
            return $"The profile name may contain at most {MaxNameLength} characters.";
        }

        if (_profiles.Any(p => p.Id != profile.Id && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return $"A profile named \"{name}\" already exists.";
        }

        if (string.IsNullOrWhiteSpace(profile.MinecraftVersion))
        {
            return "Please select a Minecraft version.";
        }

        if (!_systemInfo.IsRamAllowed(profile.RamMb))
        {
            return $"{profile.RamMb / 1024} GB RAM is not available on this PC. Maximum: {_systemInfo.ClampRam(int.MaxValue) / 1024} GB.";
        }

        if (string.IsNullOrWhiteSpace(profile.GameDirectory) || !Path.IsPathFullyQualified(profile.GameDirectory))
        {
            return "Please choose a full game directory path (for example C:\\Games\\Minecraft).";
        }

        if (profile.GameDirectory.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return "The game directory contains invalid characters.";
        }

        var args = profile.JvmArguments ?? string.Empty;
        if (args.Contains("-Xmx", StringComparison.OrdinalIgnoreCase) || args.Contains("-Xms", StringComparison.OrdinalIgnoreCase))
        {
            return "Please use the RAM setting instead of -Xmx / -Xms in the JVM arguments.";
        }

        return null;
    }

    public async Task<LauncherProfile> AddAsync(LauncherProfile profile, CancellationToken cancellationToken = default)
    {
        var copy = PrepareForSave(profile);
        if (copy.Id == Guid.Empty || _profiles.Any(p => p.Id == copy.Id))
        {
            copy.Id = Guid.NewGuid();
        }

        copy.CreatedAt = DateTimeOffset.Now;
        _profiles.Add(copy);
        await PersistAsync(cancellationToken);

        _logger.Info($"Profile created: {copy.Name}.");
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
        return copy;
    }

    public async Task UpdateAsync(LauncherProfile profile, CancellationToken cancellationToken = default)
    {
        var index = _profiles.FindIndex(p => p.Id == profile.Id);
        if (index < 0)
        {
            throw new InvalidOperationException("The profile no longer exists.");
        }

        var copy = PrepareForSave(profile);
        copy.CreatedAt = _profiles[index].CreatedAt;
        copy.LastModified = DateTimeOffset.Now;
        _profiles[index] = copy;
        await PersistAsync(cancellationToken);

        _logger.Info($"Profile updated: {copy.Name}.");
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_profiles.Count <= 1)
        {
            throw new InvalidOperationException("At least one profile must exist.");
        }

        var profile = _profiles.FirstOrDefault(p => p.Id == id)
                      ?? throw new InvalidOperationException("The profile no longer exists.");

        _profiles.Remove(profile);
        if (_selectedId == id)
        {
            _selectedId = _profiles[0].Id;
        }

        await PersistAsync(cancellationToken);

        if (_settings.Current.DefaultProfileId == id)
        {
            var settings = _settings.Current.Clone();
            settings.DefaultProfileId = null;
            await _settings.SaveAsync(settings, cancellationToken);
        }

        _logger.Info($"Profile deleted: {profile.Name}.");
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<LauncherProfile> DuplicateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var source = _profiles.FirstOrDefault(p => p.Id == id)
                     ?? throw new InvalidOperationException("The profile no longer exists.");

        var copy = source.Clone();
        copy.Id = Guid.NewGuid();
        copy.Name = UniqueName(TrimForSuffix(source.Name, " (Copy 99)") + " (Copy)");
        copy.LastModified = null;
        copy.LastLaunched = null;
        copy.GameDirectory = CreateInstanceDirectoryPath(copy.Name, copy.MinecraftVersion);
        return await AddAsync(copy, cancellationToken);
    }

    public async Task MarkLaunchedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var profile = _profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null)
        {
            return;
        }

        profile.LastLaunched = DateTimeOffset.Now;
        await PersistAsync(cancellationToken);
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>instances\{profile-slug}-{version}, unique among existing profiles.</summary>
    private string CreateInstanceDirectoryPath(string name, string version)
    {
        var root = _settings.Current.MinecraftDirectory;
        var baseName = $"{AppPaths.ToSlug(name)}-{AppPaths.ToSlug(version)}";
        var candidate = Path.Combine(root, baseName);
        for (var i = 2; _profiles.Any(p => string.Equals(p.GameDirectory, candidate, StringComparison.OrdinalIgnoreCase)); i++)
        {
            candidate = Path.Combine(root, $"{baseName}-{i}");
        }

        return candidate;
    }

    public async Task SelectAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_selectedId == id || _profiles.All(p => p.Id != id))
        {
            return;
        }

        _selectedId = id;
        await PersistAsync(cancellationToken);
        _logger.Info($"Profile selected: {SelectedProfile?.Name}.");
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    private LauncherProfile PrepareForSave(LauncherProfile profile)
    {
        var error = Validate(profile);
        if (error is not null)
        {
            throw new InvalidOperationException(error);
        }

        var copy = profile.Clone();
        copy.Name = copy.Name.Trim();
        copy.GameDirectory = copy.GameDirectory.Trim();
        copy.JvmArguments = (copy.JvmArguments ?? string.Empty).Trim();
        return copy;
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        var data = new ProfileStoreData
        {
            SelectedProfileId = _selectedId,
            Profiles = _profiles.Select(p => p.Clone()).ToList(),
        };

        using (_status.Begin(LauncherStatus.Saving, "Saving profiles…"))
        {
            await _store.SaveAsync(AppPaths.ProfilesFile, data, cancellationToken);
        }
    }

    private LauncherProfile CreateDefaultProfile()
    {
        var draft = CreateDraft();
        draft.Name = "Default";
        return draft;
    }

    /// <summary>Repairs invalid values from a hand-edited or outdated file. Returns true if changed.</summary>
    private bool Normalize(LauncherProfile profile)
    {
        var changed = false;
        var settings = _settings.Current;

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            profile.Name = "Profile";
            changed = true;
        }
        else if (profile.Name.Length > MaxNameLength)
        {
            profile.Name = profile.Name[..MaxNameLength];
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(profile.MinecraftVersion))
        {
            profile.MinecraftVersion = settings.DefaultVersion;
            changed = true;
        }

        var ram = _systemInfo.ClampRam(profile.RamMb);
        if (ram != profile.RamMb)
        {
            _logger.Warning($"Profile '{profile.Name}': {profile.RamMb} MB RAM adjusted to {ram} MB.");
            profile.RamMb = ram;
            changed = true;
        }

        if (!Enum.IsDefined(profile.JavaPreference))
        {
            profile.JavaPreference = JavaRuntimePreference.Automatic;
            changed = true;
        }

        // Missing directory, or the Phase 1 default (data root) → own instance folder.
        if (string.IsNullOrWhiteSpace(profile.GameDirectory)
            || !Path.IsPathFullyQualified(profile.GameDirectory)
            || string.Equals(Path.GetFullPath(profile.GameDirectory).TrimEnd('\\', '/'), AppPaths.Root, StringComparison.OrdinalIgnoreCase))
        {
            profile.GameDirectory = CreateInstanceDirectoryPath(profile.Name, profile.MinecraftVersion);
            changed = true;
        }

        if (profile.JvmArguments is null)
        {
            profile.JvmArguments = string.Empty;
            changed = true;
        }

        return changed;
    }

    private string UniqueName(string baseName)
    {
        if (_profiles.All(p => !string.Equals(p.Name, baseName, StringComparison.OrdinalIgnoreCase)))
        {
            return baseName;
        }

        for (var i = 2; ; i++)
        {
            var candidate = baseName.EndsWith(")", StringComparison.Ordinal) && baseName.EndsWith("(Copy)", StringComparison.Ordinal)
                ? $"{baseName[..^1]} {i})"
                : $"{baseName} {i}";
            if (_profiles.All(p => !string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }
    }

    private static string TrimForSuffix(string name, string longestSuffix)
    {
        var max = MaxNameLength - longestSuffix.Length;
        return name.Length > max ? name[..max].TrimEnd() : name;
    }
}
