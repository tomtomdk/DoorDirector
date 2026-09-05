# Changelog

All notable changes to DoorDirector are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and releases use [Semantic Versioning](https://semver.org/).

## [Unreleased]

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

[Unreleased]: https://github.com/tomtomdk/DoorDirector/compare/v0.1.2...HEAD
[0.1.2]: https://github.com/tomtomdk/DoorDirector/compare/v0.1.0...v0.1.2
[0.1.0]: https://github.com/tomtomdk/DoorDirector/releases/tag/v0.1.0
