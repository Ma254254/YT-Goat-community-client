using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GoatClient.Models;

namespace GoatClient.Services.Minecraft;

public sealed record DownloadRef(string Url, string? Path, string? Sha1, long? Size);

public sealed record AssetIndexRef(string Id, string Url, string? Sha1, long? Size);

public sealed record LibraryEntry(string Name, DownloadRef? Artifact, DownloadRef? Native, IReadOnlyList<string> NativeExcludes);

public sealed record LoggingRef(string FileId, string Url, string? Sha1, long? Size, string Argument);

/// <summary>
/// A parsed official version JSON (versions\{id}\{id}.json). All launch data comes from here –
/// libraries, natives, assets, Java requirement and version-specific arguments.
/// </summary>
public sealed class VersionManifest
{
    private VersionManifest(JsonNode root)
    {
        Root = root;
    }

    public JsonNode Root { get; }

    public required string Id { get; init; }

    public required string Type { get; init; }

    public required string MainClass { get; init; }

    public AssetIndexRef? AssetIndex { get; init; }

    public string? Assets { get; init; }

    public DownloadRef? Client { get; init; }

    public IReadOnlyList<LibraryEntry> Libraries { get; init; } = Array.Empty<LibraryEntry>();

    /// <summary>From "javaVersion". Versions without it use Mojang's legacy runtime (Java 8).</summary>
    public JavaRequirement JavaRequirement { get; init; } = new(8, "jre-legacy");

    public LoggingRef? Logging { get; init; }

    public JsonArray? GameArguments => Root["arguments"]?["game"] as JsonArray;

    public JsonArray? JvmArguments => Root["arguments"]?["jvm"] as JsonArray;

    /// <summary>Pre-1.13 format: a single space-separated argument string.</summary>
    public string? LegacyArguments => Root["minecraftArguments"]?.GetValue<string>();

    public static VersionManifest Parse(string json)
    {
        var root = JsonNode.Parse(json) ?? throw new InvalidOperationException("The version JSON is empty.");
        var id = root["id"]?.GetValue<string>() ?? throw new InvalidOperationException("The version JSON has no id.");

        if (root["inheritsFrom"] is not null)
        {
            throw new NotSupportedException("Modded versions (inheritsFrom) are not supported in this phase.");
        }

        var libraries = new List<LibraryEntry>();
        foreach (var lib in root["libraries"]?.AsArray() ?? new JsonArray())
        {
            if (lib is null || !RuleEvaluator.Allows(lib["rules"] as JsonArray, null))
            {
                continue;
            }

            var name = lib["name"]?.GetValue<string>() ?? string.Empty;
            var artifact = ReadDownload(lib["downloads"]?["artifact"]);

            DownloadRef? native = null;
            if (lib["natives"]?["windows"]?.GetValue<string>() is { } classifier)
            {
                classifier = classifier.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
                native = ReadDownload(lib["downloads"]?["classifiers"]?[classifier]);
            }

            var excludes = (lib["extract"]?["exclude"] as JsonArray)?
                .Select(e => e?.GetValue<string>())
                .OfType<string>()
                .ToList() ?? new List<string>();

            if (artifact is not null || native is not null)
            {
                libraries.Add(new LibraryEntry(name, artifact, native, excludes));
            }
        }

        var javaNode = root["javaVersion"];
        var java = javaNode?["majorVersion"] is not null
            ? new JavaRequirement(javaNode["majorVersion"]!.GetValue<int>(), javaNode["component"]?.GetValue<string>())
            : new JavaRequirement(8, "jre-legacy");

        LoggingRef? logging = null;
        var loggingNode = root["logging"]?["client"];
        if (loggingNode?["file"]?["url"]?.GetValue<string>() is { } loggingUrl && loggingNode["argument"]?.GetValue<string>() is { } argument)
        {
            logging = new LoggingRef(
                loggingNode["file"]!["id"]?.GetValue<string>() ?? "client-log4j.xml",
                loggingUrl,
                loggingNode["file"]!["sha1"]?.GetValue<string>(),
                loggingNode["file"]!["size"]?.GetValue<long>(),
                argument);
        }

        var assetIndexNode = root["assetIndex"];
        return new VersionManifest(root)
        {
            Id = id,
            Type = root["type"]?.GetValue<string>() ?? "release",
            MainClass = root["mainClass"]?.GetValue<string>() ?? throw new InvalidOperationException("The version JSON has no mainClass."),
            Assets = root["assets"]?.GetValue<string>(),
            AssetIndex = assetIndexNode?["url"]?.GetValue<string>() is { } assetUrl
                ? new AssetIndexRef(assetIndexNode["id"]?.GetValue<string>() ?? "legacy", assetUrl, assetIndexNode["sha1"]?.GetValue<string>(), assetIndexNode["size"]?.GetValue<long>())
                : null,
            Client = ReadDownload(root["downloads"]?["client"]),
            Libraries = libraries,
            JavaRequirement = java,
            Logging = logging,
        };
    }

    private static DownloadRef? ReadDownload(JsonNode? node)
    {
        var url = node?["url"]?.GetValue<string>();
        return string.IsNullOrEmpty(url)
            ? null
            : new DownloadRef(url, node?["path"]?.GetValue<string>(), node?["sha1"]?.GetValue<string>(), node?["size"]?.GetValue<long>());
    }
}

/// <summary>Evaluates Mojang library/argument rules for Windows x64.</summary>
public static class RuleEvaluator
{
    public static bool Allows(JsonArray? rules, IReadOnlyDictionary<string, bool>? features)
    {
        if (rules is null || rules.Count == 0)
        {
            return true;
        }

        var allowed = false;
        foreach (var rule in rules)
        {
            if (rule is null)
            {
                continue;
            }

            var matches = true;
            if (rule["os"] is JsonObject os)
            {
                if (os["name"]?.GetValue<string>() is { } name && name != "windows")
                {
                    matches = false;
                }

                if (os["arch"]?.GetValue<string>() is { } arch && !ArchMatches(arch))
                {
                    matches = false;
                }

                if (os["version"]?.GetValue<string>() is { } pattern
                    && !Regex.IsMatch(Environment.OSVersion.Version.ToString(), pattern))
                {
                    matches = false;
                }
            }

            if (rule["features"] is JsonObject required)
            {
                foreach (var (feature, value) in required)
                {
                    var expected = value?.GetValue<bool>() ?? false;
                    var actual = features is not null && features.TryGetValue(feature, out var enabled) && enabled;
                    if (expected != actual)
                    {
                        matches = false;
                    }
                }
            }

            if (matches)
            {
                allowed = rule["action"]?.GetValue<string>() == "allow";
            }
        }

        return allowed;
    }

    private static bool ArchMatches(string arch) => arch switch
    {
        "x86" => RuntimeInformation.OSArchitecture == Architecture.X86,
        "x64" or "amd64" => RuntimeInformation.OSArchitecture == Architecture.X64,
        "arm64" => RuntimeInformation.OSArchitecture == Architecture.Arm64,
        _ => false,
    };
}
