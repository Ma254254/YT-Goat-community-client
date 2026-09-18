// ---------------------------------------------------------------------------------------------
// Contracts for later phases. Intentionally NOT implemented and not registered anywhere –
// no fake implementations exist. Authentication, downloads, installation and launching are
// implemented in Phase 2 (Services/Auth, Downloads, Minecraft, Launch).
// ---------------------------------------------------------------------------------------------

namespace GoatClient.Services.Future;

/// <summary>Skin upload via the Minecraft services API – later phase.</summary>
public interface ISkinService
{
    Task UploadSkinAsync(string pngPath, bool slimModel, CancellationToken cancellationToken);
}

/// <summary>Launcher self-update – later phase.</summary>
public interface IUpdateService
{
    Task<bool> CheckForUpdatesAsync(CancellationToken cancellationToken);
}
