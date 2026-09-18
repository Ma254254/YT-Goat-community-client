namespace GoatClient.Services.Java;

/// <summary>Java major version required by Minecraft (as specified by Mojang).</summary>
public static class JavaRequirements
{
    public static int ForMinecraftVersion(string versionId)
    {
        if (!Version.TryParse(versionId, out var v))
        {
            return 21;
        }

        if (v >= new Version(1, 20, 5))
        {
            return 21;
        }

        if (v >= new Version(1, 18))
        {
            return 17;
        }

        return v >= new Version(1, 17) ? 16 : 8;
    }
}
