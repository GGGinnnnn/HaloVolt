param(
    [string]$Source = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Source)) {
    $Source = Join-Path $root "dist\HaloVolt-win-x64"
}

if (-not (Test-Path (Join-Path $Source "HaloVolt.exe"))) {
    throw "HaloVolt.exe not found in $Source. Run scripts\publish.ps1 first."
}

$target = Join-Path $env:LOCALAPPDATA "Programs\HaloVolt"
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item (Join-Path $Source "*") $target -Recurse -Force

$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$shortcutPath = Join-Path $startMenu "HaloVolt 环电.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $target "HaloVolt.exe"
$shortcut.WorkingDirectory = $target
$shortcut.IconLocation = (Join-Path $target "HaloVolt.exe") + ",0"
$shortcut.Description = "HaloVolt 环电 — 无线键鼠与笔记本电量托盘"
$shortcut.Save()

Write-Host "Installed to: $target"
Write-Host "Start Menu shortcut: $shortcutPath"
Write-Host "Launching HaloVolt ..."
Start-Process -FilePath (Join-Path $target "HaloVolt.exe") -ArgumentList "--open"
