# DoorDirector

DoorDirector automatically closes Valheim doors after a configurable delay. It works with ordinary doors and modded `Door` components such as gates and drawbridges.

## Features

- Automatic closing using Valheim's native networked Door state.
- Normal, inverted, and ignored behavior for individual prefabs.
- Default and per-prefab closing delays.
- An `F6` toggle for individual player-built doors and gates.
- Server-synchronized gameplay settings with configuration locking.
- Automatic cancellation when a player operates the Door again.
- Exact prefab-name diagnostics for configuring modded doors.
- Dedicated-server and BepInEx ConfigurationManager support.

## Installation

Install DoorDirector with Thunderstore Mod Manager or r2modman and enable it in the profile used to launch Valheim.

The mod must be installed on the server and every connecting client. Manual installations require BepInEx 5; place `DoorDirector.dll` in `BepInEx/plugins/DoorDirector/`.

## Configuration

Configuration is stored in `BepInEx/config/com.tomtom.doordirector.cfg`. ConfigurationManager users can edit it from the F1 menu.

Gameplay settings come from the server. When `Lock Configuration` is enabled, non-admin clients cannot change synchronized settings. Diagnostic settings and hotkey bindings remain local to each client.

### Player-built doors

Look at a door or gate you built and press `F6` to toggle auto-close for that individual object. The choice is stored on the networked object and persists with the world.

`Allow Owned Door Toggle` lets the server enable or disable this feature. `Owned Door Auto Close Delay` sets the synchronized delay used for individually enabled doors. Global disable and ignored-prefab rules always take precedence.

### Prefab rules

- `Included Prefabs`: comma-separated prefab names using normal behavior.
- `Inverted Prefabs`: comma-separated prefab names whose physical state is opposite Valheim's logical Door state.
- `Ignored Prefabs`: comma-separated prefab names DoorDirector never changes.
- `Custom Delays`: comma-separated `prefab_name=seconds` overrides.
- `Only Affect Configured Prefabs`: limits automatic closing to explicitly configured prefabs.

Example:

```ini
Included Prefabs = wood_door,iron_grate
Inverted Prefabs = my_drawbridge
Ignored Prefabs = boss_gate
Custom Delays = wood_door=3,my_drawbridge=12
```

Ignored takes precedence over inverted, and inverted takes precedence over normal. Prefab matching is case-insensitive.

## Finding prefab names

Look at a Door, gate, or bridge and press `F7`. DoorDirector prints its exact prefab name on the HUD and in the BepInEx log. This diagnostic hotkey is separate from the `F6` owned-door toggle.

You can also run this command in the Valheim console:

```text
doordirector_prefab
```

Enable `Debug Logging` to log the prefab name whenever you interact with a Door.

## License

DoorDirector is available under the [MIT License](https://github.com/tomtomdk/DoorDirector/blob/main/LICENSE).
