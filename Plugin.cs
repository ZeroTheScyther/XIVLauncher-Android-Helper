using System;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace XlaAndroidHelper;

/// <summary>
/// The in-game half of XIVLauncher Android.
///
/// The launcher app cannot show anything over the game, and the game knows nothing about the phone it runs on. This
/// plugin carries what the player needs across that line. Today that is the device's battery, in the server info
/// bar next to the world name, because a phone running FFXIV empties fast and the game has no idea.
///
/// It stays invisible anywhere else: with no launcher sending status the bar entry is simply never shown, so the
/// same build is harmless on a desktop install.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    /// <summary>A reading older than this is not shown: the launcher is gone, or the game is not under it.</summary>
    private static readonly TimeSpan Stale = TimeSpan.FromMinutes(3);

    /// <summary>At or below this, the entry carries a warning icon.</summary>
    private const int LowBattery = 15;

    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly StatusListener listener = new();
    private readonly IDtrBarEntry battery;
    private string shown = "";

    public Plugin()
    {
        battery = DtrBar.Get("XIVLauncher Android");
        battery.Shown = false;
        if (listener.Error is { } error)
            Log.Warning($"Not listening for launcher status: {error}");
        Framework.Update += OnUpdate;
    }

    private void OnUpdate(IFramework framework)
    {
        var status = listener.Latest;
        if (status == null || DateTime.UtcNow - status.Received > Stale || status.Battery < 0)
        {
            battery.Shown = false;
            shown = "";
            return;
        }

        // "Battery: 32%", with one of the game's own icons between the label and the number: lightning while
        // charging, the warning triangle when it is nearly empty. The game has no battery glyph of its own.
        var icon = status.Charging ? BitmapFontIcon.ElementLightning
            : status.Battery <= LowBattery ? BitmapFontIcon.Warning
            : BitmapFontIcon.None;
        var line = $"{icon}|{status.Battery}";
        if (line != shown)
        {
            var entry = new SeStringBuilder().AddText("Battery: ");
            if (icon != BitmapFontIcon.None)
                entry.AddIcon(icon);
            entry.AddText($"{status.Battery}%");
            battery.Text = entry.Build();
            battery.Tooltip = status.Temperature > 0
                ? $"Device battery, {status.Temperature:F0}\u00b0C"
                : "Device battery";
            shown = line;
        }
        battery.Shown = true;
    }

    public void Dispose()
    {
        Framework.Update -= OnUpdate;
        battery.Remove();
        listener.Dispose();
    }
}
