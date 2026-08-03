# Empaquette le zip portable (DESIGN.md : distribution = zip portable, pas d'installeur).
# Publie en Release, récupère les binaires natifs hors git (libmpv-2.dll, ffmpeg.exe — voir README)
# et zippe le tout dans dist/.
#
# Usage : .\package.ps1 [-Version 1.2.0] [-OutDir ..\dist]
# Version par défaut : dernier tag git, sinon "dev".
param(
    [string]$Version = "",
    [string]$OutDir = "$PSScriptRoot\..\dist"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\.."
$staging = Join-Path $env:TEMP ("wallflow-pkg-" + [guid]::NewGuid())

if (-not $Version) {
    $tag = git -C $root describe --tags --abbrev=0 2>$null
    $Version = if ($LASTEXITCODE -eq 0 -and $tag) { $tag } else { "dev" }
}

# Publie Release dans le staging (libmpv/ffmpeg y sont déjà copiés par le csproj si lib/ existe).
dotnet publish "$root\src\Wallflow\Wallflow.csproj" -c Release -o $staging
if ($LASTEXITCODE -ne 0) { throw "dotnet publish a échoué" }

# Binaires natifs hors git : échec explicite s'ils manquent (le csproj les ignore en silence).
Copy-Item "$root\lib\libmpv-2.dll" $staging -ErrorAction Stop
Copy-Item "$root\lib\ffmpeg.exe" $staging -ErrorAction Stop

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$zip = Join-Path $OutDir "wallflow-$Version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$staging\*" -DestinationPath $zip
Remove-Item $staging -Recurse -Force

Write-Output "Zip créé : $zip"
