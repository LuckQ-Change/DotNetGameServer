[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$SelfContained = "false"
)

$ErrorActionPreference = "Stop"

# 统一控制台编码为 UTF-8（由 pack.bat 的 chcp 65001 负责）

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Write-Step "Working directory: $root"

$dist = Join-Path $root "dist"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null
New-Item -ItemType Directory -Path "$dist\Window" -Force | Out-Null

Write-Step "Restore solution dependencies"
dotnet restore "$root\DotNetGameServer.sln"

Write-Step "Clean old build artifacts ($Configuration)"
dotnet clean "$root\DotNetGameServer.sln" -c $Configuration

Write-Step "Publish Window app (config=$Configuration, runtime=$Runtime, self-contained=$SelfContained)"
$sc = $false
if ($SelfContained -is [bool]) { $sc = [bool]$SelfContained }
else {
    $sc = ("$SelfContained".ToLower() -in @("true","1","yes","y","$true"))
}
$selfContainedValue = if ($sc) { "true" } else { "false" }
$publishArgs = @(
    "publish", "$root\src\Window\Window.csproj",
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $selfContainedValue,
    "-p:UseSharedCompilation=false",
    "-o", "$dist\Window"
)
dotnet $publishArgs

# 附带文档
if (Test-Path "$root\README.md") { Copy-Item "$root\README.md" "$dist\Window\" -Force }


# 打包为zip
$zipPath = Join-Path $dist ("DotNetGameServer-{0}-{1}.zip" -f $Configuration, $Runtime)
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
${ProgressPreference} = 'SilentlyContinue'
Compress-Archive -Path "$dist\Window\*" -DestinationPath $zipPath

Write-Step "Packaging done: $zipPath"