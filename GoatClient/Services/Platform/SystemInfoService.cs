using GoatClient.Core.Helpers;
using GoatClient.Models;
using GoatClient.Services.Logging;

namespace GoatClient.Services.Platform;

public sealed class SystemInfoService : ISystemInfoService
{
    /// <summary>Supported allocations: 2, 4, 6, 8, 12, 16 GB.</summary>
    public static readonly int[] SupportedRamMb = [2048, 4096, 6144, 8192, 12288, 16384];

    /// <summary>Memory kept free for Windows and other applications.</summary>
    private const int ReservedForSystemMb = 2048;

    public SystemInfoService(ILogger logger)
    {
        TotalMemoryMb = SystemMemoryInterop.GetTotalPhysicalMemoryMb();
        logger.Info(TotalMemoryMb is null
            ? "System memory could not be detected."
            : $"System memory detected: {TotalMemoryMb} MB.");
    }

    public long? TotalMemoryMb { get; }

    public int MaxAllowedRamMb => TotalMemoryMb is null
        ? SupportedRamMb[^1]
        : (int)Math.Max(SupportedRamMb[0], TotalMemoryMb.Value - ReservedForSystemMb);

    public int RecommendedRamMb => TotalMemoryMb is null or >= 8192 ? 4096 : 2048;

    public string MemorySummary => TotalMemoryMb is null
        ? "System memory could not be detected"
        : $"{Math.Round(TotalMemoryMb.Value / 1024.0)} GB system memory detected";

    public IReadOnlyList<RamOption> GetRamOptions()
        => SupportedRamMb.Select(mb => new RamOption(mb, IsRamAllowed(mb), mb == RecommendedRamMb)).ToList();

    public bool IsRamAllowed(int ramMb) => SupportedRamMb.Contains(ramMb) && ramMb <= MaxAllowedRamMb;

    public int ClampRam(int ramMb)
    {
        if (IsRamAllowed(ramMb))
        {
            return ramMb;
        }

        var allowed = SupportedRamMb.Where(IsRamAllowed).ToArray();
        var candidate = allowed.Where(mb => mb <= ramMb).DefaultIfEmpty(allowed[0]).Max();
        return candidate;
    }
}
