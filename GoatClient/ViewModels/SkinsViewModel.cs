namespace GoatClient.ViewModels;

/// <summary>Skins page. Phase 1 shows the roadmap only – no skin API, no fake skins.</summary>
public sealed class SkinsViewModel : ViewModelBase
{
    public string Title => "Skin Management";

    public string Phase => "Coming in Phase 2";

    public IReadOnlyList<string> PlannedFeatures { get; } =
    [
        "Show the skin of your signed-in Minecraft account",
        "Upload skins (classic and slim model)",
        "Local skin library for quick switching",
        "3D preview",
    ];
}
