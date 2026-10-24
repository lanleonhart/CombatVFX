# Builds the studio and the runtime and writes one zip anyone can unzip and use:
#   dist\CombatVFX-Studio-<version>.zip
#     EffectsStudio\        the studio (exe, data, recipes with examples, sounds folder)
#     Mods\CombatVFX\       the ModTek mod (dll, mod.json, settings.json, empty effects folder)
#     README.md, docs\GUIDE.md, LICENSE
#   powershell -ExecutionPolicy Bypass -File package.ps1 [-Game <BATTLETECH folder>] [-SkipStudioBuild]
param(
    [string]$Game = "",
    [switch]$SkipStudioBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $MyInvocation.MyCommand.Path -Parent
$version = (Select-String -Path (Join-Path $root 'Runtime\ModEntry.cs') -Pattern 'Version = "([^"]+)"').Matches[0].Groups[1].Value
Write-Host "CombatVFX $version"

if (-not $SkipStudioBuild) { & powershell -ExecutionPolicy Bypass -File (Join-Path $root 'Studio\build.ps1') -NoRun; if ($LASTEXITCODE -ne 0) { throw "studio build failed" } }
$gameArg = if ($Game -ne "") { "-p:BattleTechPath=`"$Game`"" } else { "" }
& dotnet build (Join-Path $root 'Runtime\CombatVFX.csproj') -c Release $gameArg | Select-String -Pattern "error|Build succeeded" | ForEach-Object { Write-Host $_.Line }
if ($LASTEXITCODE -ne 0) { throw "runtime build failed" }

$stage = Join-Path $root "dist\stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $stage 'EffectsStudio') | Out-Null
New-Item -ItemType Directory -Force (Join-Path $stage 'Mods\CombatVFX\effects') | Out-Null
New-Item -ItemType Directory -Force (Join-Path $stage 'docs') | Out-Null

# studio: everything in build except logs and this machine's settings
Get-ChildItem (Join-Path $root 'Studio\build') | Where-Object { $_.Name -notin @('studio.json', 'unity-build.log', 'shots') } | Copy-Item -Destination (Join-Path $stage 'EffectsStudio') -Recurse -Force
New-Item -ItemType Directory -Force (Join-Path $stage 'EffectsStudio\recipes\sounds') | Out-Null
Copy-Item (Join-Path $root 'Runtime\bin\Release\net472\CombatVFX.dll') (Join-Path $stage 'Mods\CombatVFX\')
Copy-Item (Join-Path $root 'Runtime\mod.json') (Join-Path $stage 'Mods\CombatVFX\')
Copy-Item (Join-Path $root 'Runtime\settings.json') (Join-Path $stage 'Mods\CombatVFX\')
Set-Content (Join-Path $stage 'Mods\CombatVFX\effects\README.txt') "Recipe files from the Effects Studio go here. A weapon tagged vfx_<file name> plays <file name>.json." -Encoding utf8
Copy-Item (Join-Path $root 'README.md') $stage
Copy-Item (Join-Path $root 'LICENSE') $stage
Copy-Item (Join-Path $root 'docs\GUIDE.md') (Join-Path $stage 'docs\')

$zip = Join-Path $root "dist\CombatVFX-Studio-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force
Write-Host "Wrote $zip ($([Math]::Round((Get-Item $zip).Length / 1MB)) MB)"
