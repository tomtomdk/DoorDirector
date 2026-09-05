[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,
    [string]$ExpectedVersion
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$resolvedPackage = (Resolve-Path -LiteralPath $PackagePath).Path
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPackage)
try {
    $requiredFiles = @("manifest.json", "README.md", "CHANGELOG.md", "icon.png", "DoorDirector.dll")
    $fileEntries = @($archive.Entries | Where-Object { -not $_.FullName.EndsWith("/") })
    $entryNames = @($fileEntries | ForEach-Object { $_.FullName })

    if (@($entryNames | Where-Object { $_ -match '[/\\]' }).Count -gt 0) {
        throw "Package files must be at the ZIP root. Found: $($entryNames -join ', ')"
    }

    $missing = @($requiredFiles | Where-Object { $_ -notin $entryNames })
    $unexpected = @($entryNames | Where-Object { $_ -notin $requiredFiles })
    if ($missing.Count -gt 0 -or $unexpected.Count -gt 0) {
        throw "Invalid package contents. Missing: [$($missing -join ', ')]. Unexpected: [$($unexpected -join ', ')]."
    }

    foreach ($name in $requiredFiles) {
        $matching = @($fileEntries | Where-Object FullName -eq $name)
        if ($matching.Count -ne 1 -or $matching[0].Length -eq 0) {
            throw "Package entry '$name' must exist exactly once and must not be empty."
        }
    }

    function Read-ZipEntryBytes([System.IO.Compression.ZipArchiveEntry]$Entry) {
        $stream = $Entry.Open()
        $memory = [System.IO.MemoryStream]::new()
        try {
            $stream.CopyTo($memory)
            return $memory.ToArray()
        }
        finally {
            $memory.Dispose()
            $stream.Dispose()
        }
    }

    $manifestEntry = $fileEntries | Where-Object FullName -eq "manifest.json"
    $manifestBytes = Read-ZipEntryBytes $manifestEntry
    $manifest = [System.Text.Encoding]::UTF8.GetString($manifestBytes) | ConvertFrom-Json
    if ($manifest.name -ne "DoorDirector") {
        throw "manifest.json name must be DoorDirector."
    }
    if ($ExpectedVersion -and $manifest.version_number -ne $ExpectedVersion) {
        throw "manifest.json version '$($manifest.version_number)' does not match '$ExpectedVersion'."
    }
    if ($manifest.dependencies -notcontains "denikson-BepInExPack_Valheim-5.4.2333") {
        throw "manifest.json is missing the required BepInEx dependency."
    }

    $iconEntry = $fileEntries | Where-Object FullName -eq "icon.png"
    $icon = Read-ZipEntryBytes $iconEntry
    $pngSignature = @(137, 80, 78, 71, 13, 10, 26, 10)
    for ($index = 0; $index -lt $pngSignature.Count; $index++) {
        if ($icon[$index] -ne $pngSignature[$index]) {
            throw "icon.png is not a valid PNG file."
        }
    }

    $width = ([int]$icon[16] * 16777216) + ([int]$icon[17] * 65536) + ([int]$icon[18] * 256) + [int]$icon[19]
    $height = ([int]$icon[20] * 16777216) + ([int]$icon[21] * 65536) + ([int]$icon[22] * 256) + [int]$icon[23]
    if ($width -ne 256 -or $height -ne 256) {
        throw "icon.png must be 256x256. Found ${width}x${height}."
    }

    $dllEntry = $fileEntries | Where-Object FullName -eq "DoorDirector.dll"
    $dll = Read-ZipEntryBytes $dllEntry
    if ($dll.Length -lt 2 -or $dll[0] -ne 0x4D -or $dll[1] -ne 0x5A) {
        throw "DoorDirector.dll does not have a valid PE header."
    }
}
finally {
    $archive.Dispose()
}

Write-Host "Validated Thunderstore package: $resolvedPackage"
