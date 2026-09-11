# Changelog

All notable changes to DoorDirector are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and releases use [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.3.0] - 2026-09-11

### Changed

- Updated and validated Harmony integration against Valheim 1.0.7's current `Door` implementation.
- Updated the required BepInExPack Valheim version to 5.4.2350 for Unity 6 and Valheim 1.0 compatibility.
- Prepared the public package README for the `TomTomDK-DoorDirector` Thunderstore listing.

## [0.2.2] - 2026-09-08

### Changed

- Marked the `Diaspora-DoorDirector` Thunderstore listing as deprecated and directed users to the new `TomTomDK-DoorDirector` listing.

## [0.2.1] - 2026-09-08

### Fixed

- Observe replicated Door state changes so switches and other mods that update ZDO state directly cancel and reschedule auto-close timers correctly.
- Prevent a timer created by a previous network owner from operating a door after ownership changes.
- Retry Valheim's native close RPC while long door or drawbridge animations temporarily reject interaction.

## [0.2.0] - 2026-09-06

### Added

- Added a configurable hotkey for toggling auto-close on individual player-built doors and gates.
- Added a server-synchronized delay for player-enabled doors.
- Added a second shortcut that opens Valheim's text input to set and persist a custom per-door delay.
- Persisted per-door overrides in Valheim's native networked object data.

## [0.1.2] - 2026-09-06

### Fixed

- Aligned the Thunderstore package, DLL, and BepInEx plugin versions after the `0.1.1` manifest-only release.

## [0.1.0] - 2026-09-06

### Added

- Automatic closing for native Valheim `Door` components.
- Normal, inverted, and ignored per-prefab behavior rules.
- Default and per-prefab close delays.
- Server-synchronized gameplay configuration with server locking.
- Cancellation when a Door is operated again before its timer expires.
- Exact prefab diagnostics through logging, an `F7` hotkey, and the `doordirector_prefab` console command.
- Dedicated-server support and a Thunderstore/r2modman package workflow.

[Unreleased]: https://github.com/tomtomdk/DoorDirector/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/tomtomdk/DoorDirector/compare/v0.2.2...v0.3.0
[0.2.2]: https://github.com/tomtomdk/DoorDirector/compare/v0.2.1...v0.2.2
[0.2.1]: https://github.com/tomtomdk/DoorDirector/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/tomtomdk/DoorDirector/compare/v0.1.2...v0.2.0
[0.1.2]: https://github.com/tomtomdk/DoorDirector/compare/v0.1.0...v0.1.2
[0.1.0]: https://github.com/tomtomdk/DoorDirector/releases/tag/v0.1.0
