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
    Write-Host '  -Single           Single window mode (--service all)'
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
$buildCfg = if ($Release) { "Release" } else { $cfg.build.configuration }
$projectPath = Join-Path $ProjectRoot $cfg.build.project

Write-Host "Building $projectPath ($buildCfg) ..."
$buildResult = dotnet build $projectPath -c $buildCfg 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed."
    exit 1
}

$tfm = "net8.0"
$dllPath = Join-Path $ProjectRoot "src\ServerHost\bin\$buildCfg\$tfm\ServerHost.dll"
if (-not (Test-Path $dllPath)) {
    $dllPath = Join-Path $ProjectRoot "src\ServerHost\bin\$buildCfg\net9.0\ServerHost.dll"
    if (Test-Path $dllPath) { $tfm = "net9.0" }
}
if (-not (Test-Path $dllPath)) {
    Write-Error "ServerHost.dll not found after build."
    exit 1
}

$env:DOTNET_ENVIRONMENT = "Development"
$env:ASPNETCORE_ENVIRONMENT = "Development"

if ($Single) {
    Write-Host "Launching all services in one window..."
    dotnet exec $dllPath -- --service all
    exit 0
}

Write-Host "Starting server instances from $dllPath ..."
foreach ($svr in $cfg.servers) {
    $type = $svr.type
    $port = $svr.port
    $healthPort = $svr.healthPort
    $healthUrl = "http://localhost:$healthPort"

    Write-Host "  Starting $type on port $port (health: $healthUrl)"
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "dotnet"
    $psi.Arguments = "exec `"$dllPath`" -- --service $type --port $port --healthCheckUrl $healthUrl"
    $psi.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Normal
    $psi.UseShellExecute = $true
    $psi.LoadUserProfile = $false
    $psi.CreateNoWindow = $false

    try {
        [System.Diagnostics.Process]::Start($psi) | Out-Null
        Start-Sleep -Milliseconds 1000
    }
    catch {
        Write-Warning "  Failed to start $type`: $($_.Exception.Message)"
    }
}

Write-Host "All servers launched."
Write-Host "NOTE: configuration is read from appsettings.json and CLI args only (no env vars)."
