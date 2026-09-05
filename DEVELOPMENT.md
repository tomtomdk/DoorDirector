# Developing DoorDirector

DoorDirector targets .NET Framework 4.8. Local builds use the actual assemblies from the installed game and BepInEx profile; no game or framework DLLs are stored in this repository.

## Local build

The default Valheim path is `D:\SteamGames\steamapps\common\Valheim`.

```powershell
.\scripts\build.ps1
```

The script locates BepInEx and ServerSync under the game installation or the default r2modman profile. Paths can also be supplied explicitly:

```powershell
.\scripts\build.ps1 `
  -ValheimPath "D:\Games\Valheim" `
  -BepInExRoot "D:\Profiles\Valheim\BepInEx" `
  -ServerSyncPath "D:\Dependencies\ServerSync.dll"
```

## Packaging

Create and validate the Thunderstore ZIP with:

```powershell
.\scripts\package.ps1
```

The package is written to `artifacts/DoorDirector-X.Y.Z.zip`. It contains `manifest.json`, `README.md`, `CHANGELOG.md`, `icon.png`, and `DoorDirector.dll` at the ZIP root.

## Versioning and releases

1. Update `DoorDirectorVersion` in `Directory.Build.props`.
2. Add the release notes to `CHANGELOG.md`.
3. Run `.\scripts\package.ps1` and inspect the ZIP in `artifacts/`.
4. Commit the changes and create a tag named `vX.Y.Z`.
5. Push the tag and publish a GitHub release for it.

GitHub Actions downloads the Valheim dedicated-server assemblies through SteamCMD, BepInExPack from Thunderstore, and the pinned upstream ServerSync release. It then compiles the mod, validates the package, uploads a workflow artifact, and attaches the ZIP to a published GitHub release.

Upload the resulting ZIP to the Valheim community on Thunderstore using the publisher namespace. Select the appropriate `Mods`, `Utility`, `Client-side`, `Server-side`, and `AI Generated` categories.

## Dependency policy

ServerSync is embedded and internalized into `DoorDirector.dll`, following its upstream deployment guidance. It is not a separate Thunderstore dependency. ConfigurationManager is an optional soft dependency.
