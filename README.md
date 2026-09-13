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

The plugin allows you to use your android's keyboard to type in game.

## Licence

AGPL-3.0. See [LICENSE](LICENSE).

Not affiliated with or endorsed by Square Enix.
