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
/// plugin carries what the player needs across that line: the device's battery in the server info bar, because a
/// phone running FFXIV empties fast and the game has no idea, and the Android keyboard, because the game cannot
/// ask a touchscreen for text on its own.
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

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static Dalamud.Plugin.Services.ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

    private readonly StatusListener listener = new();
    private readonly InputWatcher keyboard;
    private readonly IDtrBarEntry battery;
    private readonly Configuration configuration;
    private string shown = "";

    public Plugin()
    {
        configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        keyboard = new InputWatcher(listener);
        battery = DtrBar.Get("XIVLauncher Android");
        battery.Shown = false;
        battery.OnClick = ToggleUnit;
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

        // "Battery: 32% 31°C", with one of the game's own icons between the label and the number: lightning while
        // charging, the warning triangle when it is nearly empty. The game has no battery glyph of its own.
        // The temperature only appears when the launcher could read it, and clicking the entry switches its unit.
        var icon = status.Charging ? BitmapFontIcon.ElementLightning
            : status.Battery <= LowBattery ? BitmapFontIcon.Warning
            : BitmapFontIcon.None;
        var temperature = Temperature(status);
        var line = $"{icon}|{status.Battery}|{temperature}";
        if (line != shown)
        {
            var entry = new SeStringBuilder().AddText("Battery: ");
            if (icon != BitmapFontIcon.None)
                entry.AddIcon(icon);
            entry.AddText($"{status.Battery}%{temperature}");
            battery.Text = entry.Build();
            battery.Tooltip = temperature.Length > 0
                ? "Device battery. Click to switch between Celsius and Fahrenheit."
                : "Device battery";
            shown = line;
        }
        battery.Shown = true;
    }

    /// <summary>" 31\u00b0C", or nothing when the launcher could not read it.</summary>
    private string Temperature(DeviceStatus status) => status.Temperature <= 0
        ? ""
        : configuration.Fahrenheit
            ? $" {status.Temperature * 9 / 5 + 32:F0}\u00b0F"
            : $" {status.Temperature:F0}\u00b0C";

    /// <summary>Any click, because a tap on a phone arrives as a left click.</summary>
    private void ToggleUnit(DtrInteractionEvent click)
    {
        configuration.Fahrenheit = !configuration.Fahrenheit;
        PluginInterface.SavePluginConfig(configuration);
        shown = "";   // redraw on the next frame
    }

    public void Dispose()
    {
        Framework.Update -= OnUpdate;
        keyboard.Dispose();
        battery.Remove();
        listener.Dispose();
    }
}
