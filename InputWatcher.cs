using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;

namespace XlaAndroidHelper;

/// <summary>
/// Tells the launcher app when the game wants text, and writes back what the player typed on the phone.
///
/// The game runs under Wine with no idea that a touchscreen exists, and Android will not show its keyboard for a
/// field it cannot see. So the plugin watches for a text field taking input and reports across loopback; the app puts
/// a real Android input bar on screen; the finished string comes back here and goes into the field.
///
/// Two kinds of field. The game's own are found through its "text input active" flag and written with SetText.
/// Dalamud and plugin windows are ImGui, which the game knows nothing about: ImGui's WantTextInput says one is
/// active, and the text goes in through ImGui's own character input, from the draw callback, inside ImGui's frame.
///
/// Every delivery is acknowledged, so the app can type the text itself when nothing here could take it.
/// </summary>
public sealed class InputWatcher : IDisposable
{
    /// <summary>Where the app listens. Next to the mouse helper's 7946/7947 and status 7948.</summary>
    public const int AppPort = 7949;

    /// <summary>How long after a command is typed it acts, leaving time to focus a field by hand.</summary>
    private static readonly TimeSpan CommandDelay = TimeSpan.FromSeconds(5);

    private readonly UdpClient sender = new();
    private readonly object sending = new();
    private readonly IPEndPoint app = new(IPAddress.Loopback, AppPort);
    private readonly ConcurrentQueue<DeviceStatus> incoming = new();
    private readonly ConcurrentQueue<DeviceStatus> forImGui = new();
    private readonly StatusListener listener;

    /// <summary>Written by the draw callback, read by the framework update.</summary>
    private volatile bool imguiActive;

    /// <summary>
    /// The kind last reported: "game", "imgui", or null for no field. Starts as neither, so the first frame reports
    /// "no field": the app learns the plugin is here, and can open the chat itself for a line typed before any field.
    /// </summary>
    private string? reported = "unknown";
    private string? pendingVerb;
    private string pendingText = "";
    private DateTime pendingAt;

    public InputWatcher(StatusListener listener)
    {
        this.listener = listener;
        listener.TextReceived += OnTextReceived;
        Plugin.Commands.AddHandler("/xlakbd", new CommandInfo(OnCommand)
        {
            HelpMessage = "XIVLauncher Android keyboard: /xlakbd info | send <text>",
        });
        Plugin.Framework.Update += OnUpdate;
        Plugin.PluginInterface.UiBuilder.Draw += OnDraw;
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, OnAddonSetup);
    }

    /// <summary>Set once the game has built its first window: from then on its interface exists and is safe to walk.</summary>
    private volatile bool gameUiReady;

    private void OnAddonSetup(AddonEvent type, AddonArgs args) => gameUiReady = true;

    /// <summary>Arrives on the socket thread, so it is only queued here.</summary>
    private void OnTextReceived(DeviceStatus message) => incoming.Enqueue(message);

    private void OnUpdate(IFramework framework)
    {
        try
        {
            Poll();
        }
        catch (Exception ex)
        {
            // One bad frame must not take the game with it, and must not repeat every frame either.
            Plugin.Framework.Update -= OnUpdate;
            Plugin.Log.Error(ex, "keyboard watcher stopped");
        }
    }

    /// <summary>
    /// ImGui state is only valid inside ImGui's frame, so both reading WantTextInput and adding characters happen here.
    /// Characters added now are typed into the active field when ImGui starts its next frame.
    /// </summary>
    private void OnDraw()
    {
        try
        {
            var io = ImGui.GetIO();
            imguiActive = io.WantTextInput;
            while (forImGui.TryDequeue(out var message))
            {
                var ok = io.WantTextInput;
                if (ok)
                    io.AddInputCharacters(message.Text!);
                Plugin.Log.Information(ok
                    ? $"typed {message.Text!.Length} characters into an ImGui field"
                    : "no ImGui field active; text left to the app");
                Acknowledge(message.Id, ok);
            }
        }
        catch (Exception ex)
        {
            Plugin.PluginInterface.UiBuilder.Draw -= OnDraw;
            imguiActive = false;
            Plugin.Log.Error(ex, "ImGui keyboard watcher stopped");
        }
    }

    private void Poll()
    {
        // Dalamud loads plugins during the game's own start-up, on the game's main thread, before there is any
        // interface to look at. Walking it that early deadlocks the game at a black screen, so the game's fields are
        // not looked at until the game has set up its first window. Waiting for login instead missed every field
        // before it: character creation's name, for one. ImGui is safe to ask at any time: Dalamud's own windows,
        // the plugin installer among them, are up on the title screen too.
        var gameUi = gameUiReady || Plugin.ClientState.IsLoggedIn;
        var loggedIn = Plugin.ClientState.IsLoggedIn;

        while (incoming.TryDequeue(out var message))
            Deliver(message, gameUi);

        if (loggedIn)
            RunPendingCommand();

        // An ImGui window draws over the game, so a field there wins when both claim the keyboard.
        var kind = imguiActive ? "imgui" : gameUi && TextInput.IsActive() ? "game" : null;
        if (kind == reported)
            return;
        reported = kind;

        // Only here, on a change, is the interface walked: the app needs the field's limits and current contents to
        // open its keyboard on.
        var state = kind switch
        {
            "game" => TextInput.Describe(),
            "imgui" => new FieldState { Active = true, Kind = "imgui", Addon = "ImGui" },
            _ => new FieldState { Active = false },
        };
        Plugin.Log.Information(kind switch
        {
            "game" => $"text input active in \"{state.Addon}\" (max {state.MaxChar} chars, {state.MaxByte} bytes, allow {state.Allow:X4})",
            "imgui" => "text input active in an ImGui window",
            _ => "text input inactive",
        });
        Send(state);
    }

    /// <summary>Routes a string the player typed on the phone to the field it was typed for.</summary>
    private void Deliver(DeviceStatus message, bool gameUi)
    {
        if (message.Target == "imgui")
        {
            forImGui.Enqueue(message);   // written and acknowledged by the next draw
            return;
        }

        var ok = gameUi && TextInput.Write(message.Text!, out var addonName);
        Plugin.Log.Information(ok
            ? $"wrote {message.Text!.Length} characters into the game's field"
            : "no game field to write into; text left to the app");
        Acknowledge(message.Id, ok);
    }

    /// <summary>Tells the app whether its text arrived. A version 1 app sends no id and expects no answer.</summary>
    private void Acknowledge(int id, bool ok)
    {
        if (id <= 0)
            return;
        SendBytes(JsonSerializer.SerializeToUtf8Bytes(new { v = 2, ack = id, ok }));
    }

    private void Send(FieldState state) => SendBytes(JsonSerializer.SerializeToUtf8Bytes(state));

    /// <summary>From the framework update and the draw callback, so one at a time.</summary>
    private void SendBytes(byte[] datagram)
    {
        try
        {
            lock (sending)
                sender.Send(datagram, datagram.Length, app);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // No app listening: a desktop install, or the launcher is gone. Nothing to do and nothing to report
            // every frame.
        }
    }

    /// <summary>
    /// Diagnostics, for support. Submitting a command closes the chat box and drops its focus, so anything that
    /// needs a focused field has to wait for the player to open one.
    /// </summary>
    private void OnCommand(string command, string arguments)
    {
        var parts = arguments.Split(' ', 2);
        pendingVerb = parts.Length > 0 && parts[0].Length > 0 ? parts[0] : "info";
        pendingText = parts.Length > 1 ? parts[1] : "test";
        pendingAt = DateTime.Now + CommandDelay;
        Plugin.Chat.Print($"[xlakbd] {pendingVerb} in {CommandDelay.TotalSeconds:0} seconds - open a text field now");
    }

    private void RunPendingCommand()
    {
        if (pendingVerb == null || DateTime.Now < pendingAt)
            return;
        var verb = pendingVerb;
        var text = pendingText;
        pendingVerb = null;

        // A command that throws must not stop the watcher with it, so it gets its own guard.
        try
        {
            switch (verb)
            {
                case "send":
                    Plugin.Chat.Print(TextInput.Write(text, out var target)
                        ? $"[xlakbd] wrote into \"{target}\""
                        : "[xlakbd] nothing focused to write into");
                    break;

                default:
                    var state = TextInput.Describe();
                    Plugin.Chat.Print($"[xlakbd] active={state.Active} addon=\"{state.Addon}\" "
                                      + $"max={state.MaxChar}c/{state.MaxByte}b multiline={state.Multiline} "
                                      + $"allow={state.Allow:X4} app port={AppPort}"
                                      + (listener.Error is { } error ? $" listener error: {error}" : ""));
                    break;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"/xlakbd {verb} threw");
        }
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;
        Plugin.PluginInterface.UiBuilder.Draw -= OnDraw;
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, OnAddonSetup);
        Plugin.Commands.RemoveHandler("/xlakbd");
        listener.TextReceived -= OnTextReceived;
        sender.Dispose();
    }
}
