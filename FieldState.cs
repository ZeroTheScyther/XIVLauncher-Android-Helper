using System.Text.Json.Serialization;

namespace XlaAndroidHelper;

/// <summary>
/// What the plugin tells the app about the game's text input, as one small JSON datagram sent whenever it changes.
/// The app raises its keyboard on <see cref="Active"/> and hides it again when the field goes away.
/// </summary>
public sealed class FieldState
{
    /// <summary>Protocol version, so a mismatched pair can say so instead of misreading each other.</summary>
    /// <summary>
    /// 2 adds <see cref="Kind"/> and acknowledges every delivery, which is how the app knows it may fall back to
    /// typing the text itself. Version 1 never acknowledges, so the app must not fall back for it.
    /// </summary>
    [JsonPropertyName("v")]
    public int Version { get; set; } = 2;

    /// <summary>
    /// "game" for one of the game's own fields, "imgui" for a Dalamud or plugin window. An ImGui field shares neither
    /// its contents nor its limits, so the app opens on an empty line and the text is inserted at the cursor.
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "game";

    /// <summary>Whether the game is taking text right now.</summary>
    [JsonPropertyName("input")]
    public bool Active { get; set; }

    /// <summary>The window that owns the field, for logs and support. Empty when it could not be read.</summary>
    [JsonPropertyName("addon")]
    public string Addon { get; set; } = "";

    /// <summary>What the field already contains, so the keyboard opens on it rather than on an empty line.</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    /// <summary>The field's character limit, or 0 when unknown.</summary>
    [JsonPropertyName("maxChar")]
    public int MaxChar { get; set; }

    /// <summary>The field's limit in UTF-8 bytes, or 0 when unknown.</summary>
    [JsonPropertyName("maxByte")]
    public int MaxByte { get; set; }

    [JsonPropertyName("multiline")]
    public bool Multiline { get; set; }

    /// <summary>
    /// The game's own AllowedEntities flags for this field: which of uppercase, lowercase, numbers, symbols and CJK
    /// it will accept. 0 when unknown, in which case the app applies its own general filter.
    /// </summary>
    [JsonPropertyName("allow")]
    public ushort Allow { get; set; }
}
