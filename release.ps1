# Builds both plugins in Docker and writes Nexus-ready zips to release/.
# Usage: .\release.ps1   (optional: -GameDir "D:\path\to\game")
param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Gold Mining Simulator - Definitive Edition"
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$root = $PSScriptRoot

$plugins = @(
    @{ Dir = "fov-unlock"; Proj = "FovUnlock.csproj";        Dll = "FovUnlock.dll" },
    @{ Dir = "borderless"; Proj = "BorderlessWindow.csproj"; Dll = "BorderlessWindow.dll" }
)

$managed = Join-Path $GameDir "GoldMiningSimulatorDefinitiveEdition_Data\Managed"
$bie     = Join-Path $GameDir "BepInEx\core"
foreach ($p in @($managed, $bie)) {
    if (-not (Test-Path $p)) { throw "Not found: $p (pass -GameDir)" }
}

# Build all plugins in one container; DLLs land in <plugin>/out.
$cmds = @("mkdir -p /b && cp /src/Directory.Build.props /b/")
foreach ($pl in $plugins) {
    $cmds += "cp -r /src/$($pl.Dir) /b/ && rm -rf /b/$($pl.Dir)/bin /b/$($pl.Dir)/obj /b/$($pl.Dir)/out"
    $cmds += "dotnet build /b/$($pl.Dir)/$($pl.Proj) -c Release -p:ManagedDir=/managed -p:BepInExCore=/bie -o /src/$($pl.Dir)/out"
}
# PS 5.1 turns native stderr (docker pull progress) into errors under Stop.
$ErrorActionPreference = "Continue"
docker run --rm -v "${root}:/src" -v "${managed}:/managed:ro" -v "${bie}:/bie:ro" `
    mcr.microsoft.com/dotnet/sdk:8.0 bash -c ($cmds -join " && ")
$code = $LASTEXITCODE
$ErrorActionPreference = "Stop"
if ($code -ne 0) { throw "Build failed (exit $code)" }

# Zip each DLL as BepInEx/plugins/<dll> so it extracts into the game folder.
$release = Join-Path $root "release"
New-Item -ItemType Directory -Force $release | Out-Null
foreach ($pl in $plugins) {
    $src = Get-Content (Join-Path $root "$($pl.Dir)\Plugin.cs") -Raw
    if ($src -notmatch 'Version\s*=\s*"([^"]+)"') { throw "No Version in $($pl.Dir)\Plugin.cs" }
    $ver  = $Matches[1]
    $name = [IO.Path]::GetFileNameWithoutExtension($pl.Dll)

    $zip = Join-Path $release "$name-v$ver.zip"
    Remove-Item -Force $zip -ErrorAction SilentlyContinue
    # Forward-slash entry names (Compress-Archive on PS 5.1 writes backslashes).
    $z = [IO.Compression.ZipFile]::Open($zip, "Create")
    try {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z,
            (Join-Path $root "$($pl.Dir)\out\$($pl.Dll)"), "BepInEx/plugins/$($pl.Dll)") | Out-Null
    } finally { $z.Dispose() }
    Write-Host "Built $zip"
}
