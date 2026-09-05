[CmdletBinding()]
param(
    [string]$Version,
    [string]$ValheimPath,
    [string]$ValheimManagedPath,
    [string]$BepInExRoot,
    [string]$ServerSyncPath,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$propsPath = Join-Path $repoRoot "Directory.Build.props"
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$props = Get-Content -LiteralPath $propsPath -Raw
    $Version = [string]$props.Project.PropertyGroup.DoorDirectorVersion
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Thunderstore versions must use numeric SemVer format X.Y.Z. Received '$Version'."
}

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build.ps1") `
        -Configuration Release `
        -Version $Version `
        -ValheimPath $ValheimPath `
        -ValheimManagedPath $ValheimManagedPath `
        -BepInExRoot $BepInExRoot `
        -ServerSyncPath $ServerSyncPath
}

$artifactsDirectory = Join-Path $repoRoot "artifacts"
$stageDirectory = Join-Path $artifactsDirectory "thunderstore"
$zipPath = Join-Path $artifactsDirectory "DoorDirector-$Version.zip"

New-Item -ItemType Directory -Path $artifactsDirectory -Force | Out-Null
if (Test-Path -LiteralPath $stageDirectory) {
    $resolvedStage = (Resolve-Path -LiteralPath $stageDirectory).Path
    if (-not $resolvedStage.StartsWith($artifactsDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear staging directory outside artifacts: '$resolvedStage'."
    }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
New-Item -ItemType Directory -Path $stageDirectory | Out-Null

$manifestSource = Join-Path $repoRoot "thunderstore/manifest.json"
$manifest = Get-Content -LiteralPath $manifestSource -Raw | ConvertFrom-Json
$manifest.version_number = $Version
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $stageDirectory "manifest.json") -Encoding utf8

Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination $stageDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot "CHANGELOG.md") -Destination $stageDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot "thunderstore/icon.png") -Destination $stageDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot "dist/DoorDirector.dll") -Destination $stageDirectory

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $stageDirectory "*") -DestinationPath $zipPath -CompressionLevel Optimal

& (Join-Path $PSScriptRoot "validate-package.ps1") -PackagePath $zipPath -ExpectedVersion $Version
Write-Host "Created Thunderstore package: $zipPath"
