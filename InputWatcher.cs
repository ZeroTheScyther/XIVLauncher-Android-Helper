using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;

namespace XlaAndroidHelper;

/// <summary>
/// Tells the launcher app when the game wants text, and writes back what the player typed on the phone.
///
/// The game runs under Wine with no idea that a touchscreen exists, and Android will not show its keyboard for a
/// field it cannot see. So the plugin watches the game's own "text input active" flag and reports across loopback;
/// the app puts a real Android input bar on screen; the finished string comes back here and goes into the field.
///
/// Only the flag is read per frame. The interface itself is walked on a transition, and the string is written on
/// the framework thread, never from the socket thread.
/// </summary>
public sealed class InputWatcher : IDisposable
{
    /// <summary>Where the app listens. Next to the mouse helper's 7946/7947 and status 7948.</summary>
    public const int AppPort = 7949;

    /// <summary>How long after a command is typed it acts, leaving time to focus a field by hand.</summary>
    private static readonly TimeSpan CommandDelay = TimeSpan.FromSeconds(5);

    private readonly UdpClient sender = new();
    private readonly IPEndPoint app = new(IPAddress.Loopback, AppPort);
    private readonly ConcurrentQueue<string> incoming = new();
    private readonly StatusListener listener;

    private bool wasActive;
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
    }

    /// <summary>Arrives on the socket thread, so it is only queued here.</summary>
    private void OnTextReceived(string text) => incoming.Enqueue(text);

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

    private void Poll()
    {
        // Dalamud loads plugins during the game's own start-up, on the game's main thread, before there is any
        // interface to look at. Walking it that early deadlocks the game at a black screen, so nothing below runs
        // until the player is actually in the world.
        if (!Plugin.ClientState.IsLoggedIn)
        {
            if (wasActive)
            {
                wasActive = false;
                Send(new FieldState { Active = false });
            }
            return;
        }

        while (incoming.TryDequeue(out var text))
            Deliver(text);

        RunPendingCommand();

        var active = TextInput.IsActive();
        if (active == wasActive)
            return;
        wasActive = active;

        // Only here, on a change, is the interface walked: the app needs the field's limits and current contents to
        // open its keyboard on.
        var state = active ? TextInput.Describe() : new FieldState { Active = false };
        Plugin.Log.Information(active
            ? $"text input active in \"{state.Addon}\" (max {state.MaxChar} chars, {state.MaxByte} bytes, allow {state.Allow:X4})"
            : "text input inactive");
        Send(state);
    }

    /// <summary>Puts a string the player typed on the phone into the game's focused field.</summary>
    private void Deliver(string text)
    {
        if (TextInput.Write(text, out var addonName))
            Plugin.Log.Information($"wrote {text.Length} characters into \"{addonName}\"");
        else
            Plugin.Log.Warning("nothing focused to write into; text dropped");
    }

    private void Send(FieldState state)
    {
        try
        {
            var datagram = JsonSerializer.SerializeToUtf8Bytes(state);
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
        Plugin.Commands.RemoveHandler("/xlakbd");
        listener.TextReceived -= OnTextReceived;
        sender.Dispose();
    }
}
