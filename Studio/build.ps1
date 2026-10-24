# Builds the Effects Studio with the game's own Unity (2018.4.2f1) and puts recipes and a settings file next to the exe.
#   powershell -ExecutionPolicy Bypass -File E:\GNWorkshop\tools\EffectsStudio\build.ps1
param(
    [string]$Unity = "C:\Program Files\Unity\Editor\Unity.exe",
    [switch]$NoRun,
    [string]$Out = ""
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $MyInvocation.MyCommand.Path -Parent
$proj = Join-Path $root 'Unity'
if ($Out -eq "") { $Out = Join-Path $root 'build' }
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'unity-build.log'
Remove-Item $log -ErrorAction SilentlyContinue
Write-Host "Building with $Unity ..."
$p = Start-Process -FilePath $Unity -ArgumentList @('-batchmode', '-nographics', '-quit', '-projectPath', "`"$proj`"", '-executeMethod', 'BuildStudio.Build', '-logFile', "`"$log`"", '-out', "`"$Out`"") -PassThru -Wait
Write-Host "Unity exit code $($p.ExitCode)"
if (Test-Path $log) {
    $errors = Select-String -Path $log -Pattern 'error CS|\[BuildStudio\]|Build.*Failed|Exception' | Select-Object -First 30
    $errors | ForEach-Object { Write-Host $_.Line.Substring(0, [Math]::Min(220, $_.Line.Length)) }
}
$exe = Join-Path $Out 'EffectsStudio.exe'
if (Test-Path $exe) {
    New-Item -ItemType Directory -Force (Join-Path $Out 'recipes') | Out-Null
    Copy-Item (Join-Path $root 'recipes\*.json') (Join-Path $Out 'recipes') -Force
    Write-Host "Built: $exe"
    if (-not $NoRun) { Start-Process $exe }
} else { Write-Host "No executable produced; see $log"; exit 1 }
