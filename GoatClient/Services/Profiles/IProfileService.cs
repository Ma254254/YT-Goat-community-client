using GoatClient.Models;

namespace GoatClient.Services.Profiles;

public interface IProfileService
{
    IReadOnlyList<LauncherProfile> Profiles { get; }

    LauncherProfile? SelectedProfile { get; }

    event EventHandler? ProfilesChanged;

    Task LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>A new, unsaved profile pre-filled with the default settings.</summary>
    LauncherProfile CreateDraft();

    /// <summary>Returns an error message, or null if the profile is valid.</summary>
    string? Validate(LauncherProfile profile);

    Task<LauncherProfile> AddAsync(LauncherProfile profile, CancellationToken cancellationToken = default);

    Task UpdateAsync(LauncherProfile profile, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<LauncherProfile> DuplicateAsync(Guid id, CancellationToken cancellationToken = default);

    Task SelectAsync(Guid id, CancellationToken cancellationToken = default);
}
