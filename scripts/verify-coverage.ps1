param(
    [Parameter(Mandatory = $false)]
    [string]$ResultsDirectory = "TestResults",

    [Parameter(Mandatory = $false)]
    [double]$MinimumLineRate = 0.60,

    [Parameter(Mandatory = $false)]
    [double]$MinimumBranchRate = 0.45
)

$ErrorActionPreference = "Stop"
$coverageFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -Filter "coverage.cobertura.xml")
if ($coverageFiles.Count -eq 0) {
    throw "No coverage.cobertura.xml was found under $ResultsDirectory."
}

$linesCovered = 0L
$linesValid = 0L
$branchesCovered = 0L
$branchesValid = 0L
foreach ($file in $coverageFiles) {
    [xml]$report = Get-Content -LiteralPath $file.FullName -Raw
    $linesCovered += [long]$report.coverage."lines-covered"
    $linesValid += [long]$report.coverage."lines-valid"
    $branchesCovered += [long]$report.coverage."branches-covered"
    $branchesValid += [long]$report.coverage."branches-valid"
}

$lineRate = if ($linesValid -eq 0) { 0 } else { $linesCovered / $linesValid }
$branchRate = if ($branchesValid -eq 0) { 0 } else { $branchesCovered / $branchesValid }
Write-Host ("Coverage: lines={0:P2}, branches={1:P2}" -f $lineRate, $branchRate)

if ($lineRate -lt $MinimumLineRate) {
    throw ("Line coverage {0:P2} is below the required {1:P2}." -f $lineRate, $MinimumLineRate)
}
if ($branchRate -lt $MinimumBranchRate) {
    throw ("Branch coverage {0:P2} is below the required {1:P2}." -f $branchRate, $MinimumBranchRate)
}
