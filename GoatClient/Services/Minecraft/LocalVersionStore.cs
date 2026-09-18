using System.IO;
using System.Text.Json.Nodes;
using GoatClient.Core.Constants;

namespace GoatClient.Services.Minecraft;

/// <summary>Paths and quick checks for locally installed versions (versions\{id}\).</summary>
public static class LocalVersionStore
{
    private const string MarkerFileName = ".goatclient-installed";

    public static string GetVersionDirectory(string id) => Path.Combine(AppPaths.Versions, id);

    public static string GetJsonPath(string id) => Path.Combine(GetVersionDirectory(id), id + ".json");

    public static string GetJarPath(string id) => Path.Combine(GetVersionDirectory(id), id + ".jar");

    public static string GetNativesDirectory(string id) => Path.Combine(GetVersionDirectory(id), "natives");

    public static string GetMarkerPath(string id) => Path.Combine(GetVersionDirectory(id), MarkerFileName);

    /// <summary>
    /// Fast check: the installation completed (marker written after all files were verified)
    /// and the core files still exist. A full hash check is done by "Repair".
    /// </summary>
    public static bool IsInstalled(string id)
        => File.Exists(GetMarkerPath(id)) && File.Exists(GetJsonPath(id)) && File.Exists(GetJarPath(id));

    public static void MarkInstalled(string id)
        => File.WriteAllText(GetMarkerPath(id), DateTimeOffset.Now.ToString("O"));

    public static void ClearInstalled(string id)
    {
        var marker = GetMarkerPath(id);
        if (File.Exists(marker))
        {
            File.Delete(marker);
        }
    }

    /// <summary>Reads javaVersion.majorVersion from the local version JSON, if present.</summary>
    public static int? TryReadJavaMajor(string id)
    {
        try
        {
            var path = GetJsonPath(id);
            if (!File.Exists(path))
            {
                return null;
            }

            var node = JsonNode.Parse(File.ReadAllText(path));
            return node?["javaVersion"]?["majorVersion"]?.GetValue<int>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static IEnumerable<string> EnumerateInstalledIds()
    {
        if (!Directory.Exists(AppPaths.Versions))
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(AppPaths.Versions))
        {
            var id = Path.GetFileName(directory);
            if (IsInstalled(id))
            {
                yield return id;
            }
        }
    }
}
