# XIVLauncher Android Helper

A Dalamud plugin that connects FINAL FANTASY XIV to the [XIVLauncher Android](https://github.com/ZeroTheScyther/XIVLauncher-Android)
app.


## Repository

Add this to Dalamud's custom plugin repositories, or let the launcher add it for you:

```
https://raw.githubusercontent.com/ZeroTheScyther/XIVLauncher-Android-Helper/main/repo.json
```

## Building

Needs the .NET 10 SDK and a Dalamud dev distribution.

```sh
DALAMUD_HOME=~/.xlcore/dalamud/Hooks/dev dotnet build XlaAndroidHelper.csproj -c Release
```

## Protocol

The launcher sends one UDP datagram to `127.0.0.1:7948` whenever the device status changes, and at least every 45
seconds:

```json
{ "v": 1, "battery": 32, "charging": false, "temperature": 31.5 }
```

`battery` is a percentage, or -1 when unknown. `temperature` can be toggled between Fahrenheit and Celsius by tapping the battery indicator.

### Keyboard

The game cannot ask a touchscreen for text, so the plugin reports when a text field takes focus and writes back what
the player typed on the phone.

When the focused field changes, the plugin sends one datagram to `127.0.0.1:7949`:

```json
{ "v": 1, "input": true, "addon": "ChatLog", "text": "", "maxChar": 0, "maxByte": 500, "multiline": false, "allow": 639 }
```

`maxChar` and `maxByte` are the field's own limits, 0 when unknown. `allow` is the field's AllowedEntities flags,
which say which characters it accepts. `{ "v": 1, "input": false }` follows when the field loses focus.

The app sends the finished line back to `127.0.0.1:7948`:

```json
{ "v": 1, "text": "hello" }
```

It is written into the focused field on the next frame. The app presses Enter itself, as a key.

`/xlakbd` reports the focused field, and `/xlakbd send <text>` writes into it. Both act five seconds after the
command, because submitting a command closes the chat box and drops its focus.

## Licence

AGPL-3.0. See [LICENSE](LICENSE).

Not affiliated with or endorsed by Square Enix.
