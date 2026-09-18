namespace GoatClient.Models;

/// <summary>One file to download. Sha1/Size come from official metadata when available.</summary>
public sealed record DownloadRequest(string Url, string Path, string? Sha1, long? Size);

/// <summary>Progress of a multi-file operation (download, verification, installation).</summary>
public sealed record TransferProgress(
    string Stage,
    long BytesDone,
    long BytesTotal,
    int FilesDone,
    int FilesTotal,
    double BytesPerSecond,
    bool IsIndeterminate = false)
{
    public double Fraction => BytesTotal > 0
        ? Math.Clamp((double)BytesDone / BytesTotal, 0, 1)
        : FilesTotal > 0 ? Math.Clamp((double)FilesDone / FilesTotal, 0, 1) : 0;

    public static TransferProgress Indeterminate(string stage) => new(stage, 0, 0, 0, 0, 0, true);
}

public enum MinecraftProcessState
{
    NotRunning,
    Starting,
    Running,
    Exited,
    Crashed,
}
