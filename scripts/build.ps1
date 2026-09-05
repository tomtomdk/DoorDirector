[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Version,
    [string]$ValheimPath,
    [string]$ValheimManagedPath,
    [string]$BepInExRoot,
    [string]$ServerSyncPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $repoRoot "DoorDirector/DoorDirector.csproj"
$propsPath = Join-Path $repoRoot "Directory.Build.props"

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$props = Get-Content -LiteralPath $propsPath -Raw
    $Version = [string]$props.Project.PropertyGroup.DoorDirectorVersion
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must use numeric SemVer format X.Y.Z. Received '$Version'."
}

$arguments = @(
    "build",
    $projectPath,
    "--configuration", $Configuration,
    "--no-incremental",
    "--nologo",
    "/p:Version=$Version"
)

$optionalProperties = @{
    ValheimPath = $ValheimPath
    ValheimManagedPath = $ValheimManagedPath
    BepInExRoot = $BepInExRoot
    ServerSyncPath = $ServerSyncPath
}

foreach ($property in $optionalProperties.GetEnumerator()) {
    if (-not [string]::IsNullOrWhiteSpace($property.Value)) {
        $arguments += "/p:$($property.Key)=$($property.Value)"
    }
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "DoorDirector build failed with exit code $LASTEXITCODE."
}

$builtDll = Join-Path $repoRoot "DoorDirector/bin/$Configuration/DoorDirector.dll"
if (-not (Test-Path -LiteralPath $builtDll -PathType Leaf)) {
    throw "Build succeeded but DoorDirector.dll was not found at '$builtDll'."
}

$distDirectory = Join-Path $repoRoot "dist"
New-Item -ItemType Directory -Path $distDirectory -Force | Out-Null
$distDll = Join-Path $distDirectory "DoorDirector.dll"
Copy-Item -LiteralPath $builtDll -Destination $distDll -Force

Write-Host "Built DoorDirector ${Version}: $distDll"
