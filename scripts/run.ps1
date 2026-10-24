param(
    [switch]$Demo
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\HaloVolt\HaloVolt.csproj"
$runArgs = @("run", "--project", $project, "-c", "Debug")
if ($Demo) {
    $runArgs += @("--", "--demo", "--open")
}
& dotnet @runArgs
