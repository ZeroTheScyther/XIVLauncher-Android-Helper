using System;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XlaAndroidHelper;

/// <summary>
/// The game side of the Android keyboard: finding the text field the player is typing in, describing it, and
/// writing a finished string into it.
///
/// Everything here reads or writes game memory and must run on the framework thread. Nothing here may run during
/// plugin construction: Dalamud loads plugins while the game is still starting up, and walking the interface that
/// early deadlocks the game's main thread.
/// </summary>
public static unsafe class TextInput
{
    /// <summary>Whether the game currently has a text field taking input. One flag read, cheap enough per frame.</summary>
    public static bool IsActive()
    {
        var module = Module();
        return module != null && module->IsTextInputActive();
    }

    /// <summary>
    /// The text field the game has focus on, or null. Found through the focused addon's node list: a component node
    /// whose component reports itself as a text input.
    /// </summary>
    public static AtkComponentTextInput* Focused(out string addonName)
    {
        addonName = "";
        var stage = AtkStage.Instance();
        if (stage == null || stage->RaptureAtkUnitManager == null)
            return null;
        var addon = stage->RaptureAtkUnitManager->AtkUnitManager.FocusedAddon;
        if (addon == null)
            return null;
        addonName = addon->NameString;

        var nodes = addon->UldManager.NodeList;
        if (nodes == null)
            return null;
        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = nodes[i];
            if (node == null || (int)node->Type < 1000)
                continue;   // only component nodes carry a component
            var component = ((AtkComponentNode*)node)->Component;
            if (component != null && component->GetComponentType() == ComponentType.TextInput)
                return (AtkComponentTextInput*)component;
        }
        return null;
    }

    /// <summary>What the app needs to put a correct keyboard on screen, read from the focused field itself.</summary>
    public static FieldState Describe()
    {
        var state = new FieldState { Active = IsActive() };
        if (!state.Active)
            return state;

        var field = Focused(out var addonName);
        state.Addon = addonName;
        if (field == null)
            return state;   // active, but the field is somewhere we cannot read: the app falls back to its defaults

        // The field carries its own limits, so the keyboard can cap length and drop characters the field rejects
        // before anything is sent, rather than having the game silently refuse the string.
        var data = field->ComponentTextData;
        state.MaxChar = (int)data.MaxChar;
        state.MaxByte = (int)data.MaxByte;
        state.Multiline = data.MaxLine > 1;
        state.Allow = (ushort)field->InputSanitizationFlags;
        state.Text = field->RawString.ToString();
        return state;
    }

    /// <summary>
    /// Replaces the focused field's contents with <paramref name="text"/>, trimmed to what the field accepts.
    /// Returns false when there is nothing focused to write to.
    ///
    /// SetText rather than InsertText: the player composes the whole line on the phone and sends it in one go, so
    /// there is no cursor to respect. Verified on device.
    /// </summary>
    public static bool Write(string text, out string addonName)
    {
        var field = Focused(out addonName);
        if (field == null)
            return false;

        var data = field->ComponentTextData;
        field->SetText(Trim(text, (int)data.MaxChar, (int)data.MaxByte));
        return true;
    }

    /// <summary>
    /// Cuts a string to the field's own limits. Bytes are counted as UTF-8, and a character is never split in
    /// half: a string cut mid-character is worse than a short one.
    /// </summary>
    public static string Trim(string text, int maxChar, int maxByte)
    {
        if (maxChar > 0 && text.Length > maxChar)
            text = text[..maxChar];
        if (maxByte <= 0 || Encoding.UTF8.GetByteCount(text) <= maxByte)
            return text;

        var bytes = 0;
        var end = 0;
        var e = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
        {
            var element = (string)e.Current;
            var size = Encoding.UTF8.GetByteCount(element);
            if (bytes + size > maxByte)
                break;
            bytes += size;
            end += element.Length;
        }
        return text[..end];
    }

    private static AtkModule* Module()
    {
        var rapture = RaptureAtkModule.Instance();
        return rapture == null ? null : &rapture->AtkModule;
    }
}
