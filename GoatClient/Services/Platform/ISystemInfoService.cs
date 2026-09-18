using GoatClient.Models;

namespace GoatClient.Services.Platform;

/// <summary>Information about the local machine (memory) and RAM rules for Minecraft.</summary>
public interface ISystemInfoService
{
    /// <summary>Installed physical memory in MB, null if unknown.</summary>
    long? TotalMemoryMb { get; }

    int RecommendedRamMb { get; }

    int MaxAllowedRamMb { get; }

    string MemorySummary { get; }

    IReadOnlyList<RamOption> GetRamOptions();

    bool IsRamAllowed(int ramMb);

    /// <summary>Returns the closest supported and allowed value.</summary>
    int ClampRam(int ramMb);
}
