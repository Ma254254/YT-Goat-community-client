namespace GoatClient.Services.Platform;

public interface IShellService
{
    /// <summary>Opens a folder in Windows Explorer. Returns false if it does not exist.</summary>
    bool OpenFolder(string path);

    /// <summary>Opens an https URL in the default browser.</summary>
    void OpenUrl(string url);
}
