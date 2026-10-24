param(
    [switch]$SelfContained,
    [string]$Configuration = "Release",
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\HaloVolt\HaloVolt.csproj"
$dist = Join-Path $root "dist"
$name = if ($SelfContained) { "HaloVolt-portable-win-x64" } else { "HaloVolt-win-x64" }
$out = Join-Path $dist $name

if (Get-Process HaloVolt -ErrorAction SilentlyContinue) {
    throw "HaloVolt is running. Close it before publishing so the executable is not locked."
}

New-Item -ItemType Directory -Path $out -Force | Out-Null
Write-Host "Publishing $name ..."
$publishArgs = @(
    "publish", $project,
    "-c", $Configuration,
    "-r", "win-x64",
    "--self-contained", $(if ($SelfContained) { "true" } else { "false" }),
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-o", $out
)
if ($SelfContained) {
    $publishArgs += "-p:EnableCompressionInSingleFile=true"
}
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Copy-Item (Join-Path $root "README.md") $out -Force
Copy-Item (Join-Path $root "LICENSE") $out -Force
$docs = Join-Path $out "docs\screenshots"
New-Item -ItemType Directory -Path $docs -Force | Out-Null
Copy-Item (Join-Path $root "docs\screenshots\*") $docs -Force
Copy-Item (Join-Path $root "docs\*.md") (Join-Path $out "docs") -Force

if (-not $NoZip) {
    $zip = Join-Path $dist ($name + ".zip")
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "Zip: $zip"
}

$exe = Join-Path $out "HaloVolt.exe"
if (Test-Path $exe) {
    $size = [math]::Round((Get-Item $exe).Length / 1MB, 2)
    Write-Host "Done: $exe ($size MB)"
} else {
    throw "Publish completed but HaloVolt.exe was not produced."
}
