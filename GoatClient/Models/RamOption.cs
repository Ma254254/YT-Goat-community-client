namespace GoatClient.Models;

/// <summary>A selectable memory allocation.</summary>
public sealed record RamOption(int Mb, bool IsAllowed, bool IsRecommended)
{
    public int Gb => Mb / 1024;

    public string Label
    {
        get
        {
            var label = $"{Gb} GB";
            if (IsRecommended)
            {
                label += "  (Recommended)";
            }

            if (!IsAllowed)
            {
                label += "  (Too much for this PC)";
            }

            return label;
        }
    }
}
