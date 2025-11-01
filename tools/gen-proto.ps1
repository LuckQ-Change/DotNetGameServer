param(
    [string]$ProtoDir = "",
    [string]$OutputDir = "",
    [switch]$IncludeGrpc
)

$ErrorActionPreference = "Stop"

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Warn($msg) { Write-Host "[warn] $msg" -ForegroundColor Yellow }

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionRoot = Split-Path -Parent $root

if ([string]::IsNullOrWhiteSpace($ProtoDir)) {
    $ProtoDir = Join-Path $solutionRoot "Shared\Protos"
}
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $solutionRoot "artifacts\proto-gen"
}

Write-Step "ProtoDir: $ProtoDir"
Write-Step "OutputDir: $OutputDir"

if (-not (Test-Path $ProtoDir)) { throw "ProtoDir not found: $ProtoDir" }
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# Locate protoc from Grpc.Tools nuget cache
$nugetGrpcRoot = Join-Path $env:USERPROFILE ".nuget\packages\grpc.tools"
if (-not (Test-Path $nugetGrpcRoot)) { throw "Grpc.Tools not found in $nugetGrpcRoot. Please run 'dotnet restore' first." }

$versions = Get-ChildItem -Directory $nugetGrpcRoot | Sort-Object Name -Descending
if ($versions.Count -eq 0) { throw "No versions found under $nugetGrpcRoot" }
$useVersion = $versions[0]

$protoc64 = Join-Path $useVersion.FullName "tools\windows_x64\protoc.exe"
$protoc32 = Join-Path $useVersion.FullName "tools\windows_x86\protoc.exe"

if (Test-Path $protoc64) { $protoc = $protoc64 }
elseif (Test-Path $protoc32) { $protoc = $protoc32 }
else { throw "protoc.exe not found in Grpc.Tools $($useVersion.Name)" }

Write-Step "Using protoc: $protoc"

if ($IncludeGrpc) {
    $grpcPlugin64 = Join-Path $useVersion.FullName "tools\windows_x64\grpc_csharp_plugin.exe"
    $grpcPlugin32 = Join-Path $useVersion.FullName "tools\windows_x86\grpc_csharp_plugin.exe"
    if (Test-Path $grpcPlugin64) { $grpcPlugin = $grpcPlugin64 }
    elseif (Test-Path $grpcPlugin32) { $grpcPlugin = $grpcPlugin32 }
    else { throw "grpc_csharp_plugin.exe not found in Grpc.Tools $($useVersion.Name)" }
    Write-Step "Using grpc plugin: $grpcPlugin"
}

$protoFiles = Get-ChildItem -Path $ProtoDir -Filter *.proto -File
if ($protoFiles.Count -eq 0) { throw "No .proto files found in $ProtoDir" }

foreach ($f in $protoFiles) {
    Write-Step "Generating: $($f.Name)"
    $args = @(
        "-I", $ProtoDir,
        "--csharp_out=$OutputDir",
        $f.FullName
    )
    & $protoc $args

    if ($IncludeGrpc) {
        $argsGrpc = @(
            "-I", $ProtoDir,
            "--grpc_out=$OutputDir",
            "--plugin=protoc-gen-grpc=$grpcPlugin",
            $f.FullName
        )
        & $protoc $argsGrpc
    }
}

Write-Step "Done. Generated files in: $OutputDir"