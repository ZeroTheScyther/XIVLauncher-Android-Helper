using Dalamud.Configuration;

namespace XlaAndroidHelper;

/// <summary>What the player chose, kept in Dalamud's own config folder.</summary>
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Temperature unit for the battery indicator. Toggled by tapping it.</summary>
    public bool Fahrenheit { get; set; }
}
