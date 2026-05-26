param(
    [string]$Config = "",
    [switch]$Single,
    [switch]$Release,
    [switch]$Help
)

$ErrorActionPreference = "Stop"

if ($Help) {
    Write-Host 'Usage: .\scripts\start-all.ps1 [options]'
    Write-Host '  -Config <path>    servers.json path (default: scripts/servers.json)'
    Write-Host '  -Single           Run all servers in the current window (no separate windows)'
    Write-Host '  -Release          Release build (default: Debug)'
    Write-Host '  -Help             Show this help'
    exit 0
}

$ProjectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$ConfigPath = if ($Config) { $Config } else { Join-Path $ProjectRoot "scripts\servers.json" }

if (-not (Test-Path $ConfigPath)) {
    Write-Error "Config file not found: $ConfigPath"
    exit 1
}

$cfg = Get-Content $ConfigPath -Raw | ConvertFrom-Json
$buildCfg = if ($Release) { "Release" } elseif ($cfg.build.configuration) { $cfg.build.configuration } else { "Debug" }
$tfm = "net8.0"

# Build the whole solution once; each server is its own executable project.
$solution = if ($cfg.build.solution) { $cfg.build.solution } else { "DotNetGameServer.sln" }
$solutionPath = Join-Path $ProjectRoot $solution
Write-Host "Building $solutionPath ($buildCfg) ..."
dotnet build $solutionPath -c $buildCfg
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed."
    exit 1
}

$env:DOTNET_ENVIRONMENT = "Development"
$env:ASPNETCORE_ENVIRONMENT = "Development"

function Get-DllPath($svr) {
    $projDir = Split-Path -Parent (Join-Path $ProjectRoot $svr.project)
    return Join-Path $projDir "bin\$buildCfg\$tfm\$($svr.dll)"
}

$procs = @()
foreach ($svr in $cfg.servers) {
    $dllPath = Get-DllPath $svr
    if (-not (Test-Path $dllPath)) {
        Write-Warning "  Missing dll: $dllPath (skipped)"
        continue
    }

    $healthUrl = "http://localhost:$($svr.healthPort)"
    $argList = @("exec", "`"$dllPath`"", "--", "--port", "$($svr.port)", "--healthCheckUrl", $healthUrl)
    Write-Host "  Starting $($svr.name) on port $($svr.port) (health: $healthUrl)"

    try {
        if ($Single) {
            $procs += Start-Process -FilePath "dotnet" -ArgumentList $argList -NoNewWindow -PassThru
        }
        else {
            Start-Process -FilePath "dotnet" -ArgumentList $argList -PassThru | Out-Null
            Start-Sleep -Milliseconds 800
        }
    }
    catch {
        Write-Warning "  Failed to start $($svr.name): $($_.Exception.Message)"
    }
}

if ($Single) {
    Write-Host "All servers running in this window. Press Ctrl+C to stop."
    if ($procs.Count -gt 0) { Wait-Process -Id ($procs.Id) }
}
else {
    Write-Host "All servers launched in separate windows."
}
Write-Host "NOTE: each server reads its own appsettings.json + CLI args only (no env vars)."
