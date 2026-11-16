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
    /// The text field the game has focus on, or null. Found in the focused addon's node list: the text input the
    /// game's text input manager is targeting. An addon can hold several (Party Finder has Keywords and Excluded
    /// Words), and the first in the list is not necessarily the one with the cursor.
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

        var input = stage->AtkInputManager;
        var target = input == null || input->TextInput == null ? null : input->TextInput->TargetTextInputEventInterface;
        var focusedNode = input == null ? null : input->FocusedNode;

        var nodes = addon->UldManager.NodeList;
        if (nodes == null)
            return null;
        AtkComponentTextInput* first = null;
        AtkComponentTextInput* holdsFocus = null;
        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = nodes[i];
            if (node == null || (int)node->Type < 1000)
                continue;   // only component nodes carry a component
            var component = ((AtkComponentNode*)node)->Component;
            if (component == null || component->GetComponentType() != ComponentType.TextInput)
                continue;
            var field = (AtkComponentTextInput*)component;
            if (target != null && target == &field->AtkTextInputEventInterface)
                return field;
            if (holdsFocus == null && focusedNode != null
                && (field->OwnerNode == focusedNode || component->IsOwnerNodeAncestorOf(focusedNode)))
                holdsFocus = field;
            if (first == null)
                first = field;
        }
        return holdsFocus != null ? holdsFocus : first;
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
    /// </summary>
    public static bool Write(string text, out string addonName)
    {
        var field = Focused(out addonName);
        if (field == null)
            return false;

        // InsertText, the path typing and pasting take, rather than SetText: SetText only changes what the field
        // shows. Windows like Party Finder keep their own copy of the value, updated by that path, and redraw the
        // field from it on deselect, so set text vanished (while still sitting in the field's history).
        var data = field->ComponentTextData;
        field->SetText("");
        field->InsertText(Trim(text, (int)data.MaxChar, (int)data.MaxByte), false);
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
