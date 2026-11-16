using System;
using System.Text.Json.Serialization;

namespace XlaAndroidHelper;

/// <summary>
/// What the launcher app reports about the device it is running on. Sent as one small JSON datagram whenever
/// something changes, and at least once a minute so a stale reading can be spotted.
/// </summary>
public sealed class DeviceStatus
{
    /// <summary>Protocol version. The app and the plugin ship together, but a mismatch must not throw.</summary>
    [JsonPropertyName("v")]
    public int Version { get; set; }

    /// <summary>Battery charge, 0-100, or -1 when the app could not read it.</summary>
    [JsonPropertyName("battery")]
    public int Battery { get; set; } = -1;

    [JsonPropertyName("charging")]
    public bool Charging { get; set; }

    /// <summary>Battery temperature in degrees Celsius, or 0 when unknown.</summary>
    [JsonPropertyName("temperature")]
    public float Temperature { get; set; }

    /// <summary>
    /// Set when the datagram carries text the player typed on the phone instead of a battery reading. The two share
    /// a port because they share a direction: everything the app sends the game arrives here.
    /// </summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>Which field the text is for: "game" (the default, and all a version 1 app sends) or "imgui".</summary>
    [JsonPropertyName("target")]
    public string? Target { get; set; }

    /// <summary>Echoed back in the acknowledgement. 0 from a version 1 app, which expects none.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>When this reading arrived, by the game's clock.</summary>
    [JsonIgnore]
    public DateTime Received { get; set; }
}
