# XIVLauncher Android Helper

A Dalamud plugin that connects FINAL FANTASY XIV to the [XIVLauncher Android](https://github.com/ZeroTheScyther/XIVLauncher-Android)
app it is running under.

Today it shows the device's battery in the server info bar. Android denies apps access to the battery sysfs, so Wine
reports no battery at all and nothing in game can see one. The launcher app reads it and sends it over loopback.

The plugin is installed and kept up to date by the launcher app. It does nothing on a desktop install: with no
launcher sending status, the info bar entry never appears.

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

The build writes `bin/Release/XlaAndroidHelper/latest.zip`, which is what a release ships.

## Protocol

The launcher sends one UDP datagram to `127.0.0.1:7948` whenever the device status changes, and at least every 45
seconds:

```json
{ "v": 1, "battery": 32, "charging": false, "temperature": 31.5 }
```

`battery` is a percentage, or -1 when unknown. `temperature` is degrees Celsius, or 0 when unknown. A reading older
than three minutes is treated as no launcher, and the info bar entry hides itself.

## Licence

AGPL-3.0. See [LICENSE](LICENSE).

Not affiliated with or endorsed by Square Enix.
