using System.IO;
using System.Text.Json;
using GoatClient.Services.Logging;

namespace GoatClient.Services.Auth;

/// <summary>
/// The Microsoft application (client) ID of GOAT CLIENT.
/// It belongs to the developer's Azure app and ships with the launcher in
/// <c>microsoft-auth.json</c> next to GoatClient.exe (like every launcher with its own login).
/// It is public – not a secret. A value in Settings (advanced) overrides the file.
/// </summary>
public sealed class MicrosoftAuthConfig
{
    public const string FileName = "microsoft-auth.json";

    private MicrosoftAuthConfig(string? clientId, string source)
    {
        ClientId = clientId;
        Source = source;
    }

    /// <summary>Validated client ID from the file, or null.</summary>
    public string? ClientId { get; }

    /// <summary>Human-readable description of where the ID came from.</summary>
    public string Source { get; }

    public static MicrosoftAuthConfig LoadBundled(ILogger logger)
        => Load(Path.Combine(AppContext.BaseDirectory, FileName), logger);

    public static MicrosoftAuthConfig Load(string path, ILogger logger)
    {
        if (!File.Exists(path))
        {
            logger.Info($"{FileName} not found – no built-in Microsoft app ID.");
            return new MicrosoftAuthConfig(null, "not configured");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var value = document.RootElement.TryGetProperty("clientId", out var element) && element.ValueKind == JsonValueKind.String
                ? element.GetString()?.Trim()
                : null;

            if (string.IsNullOrEmpty(value))
            {
                return new MicrosoftAuthConfig(null, "not configured");
            }

            if (!Guid.TryParse(value, out _))
            {
                logger.Warning($"{FileName}: 'clientId' is not a valid GUID and is ignored.");
                return new MicrosoftAuthConfig(null, "invalid value in " + FileName);
            }

            logger.Info($"Built-in Microsoft app ID loaded from {FileName}.");
            return new MicrosoftAuthConfig(value, "built-in (" + FileName + ")");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.Error($"{FileName} could not be read – no built-in Microsoft app ID.", ex);
            return new MicrosoftAuthConfig(null, "unreadable " + FileName);
        }
    }
}
