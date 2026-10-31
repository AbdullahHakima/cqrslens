#Requires -Version 5.1
<#
.SYNOPSIS
    Runs repeatable CqrsLens warm and cold-process benchmarks.

.DESCRIPTION
    Builds the benchmark harness once, then measures:
      * warm analysis in a single process (JIT and caches warmed)
      * cold analysis in a fresh process per sample (first-call JIT included)

    Use -Generate N to benchmark a deterministic synthetic solution with N
    request/handler slices instead of the checked-in SampleShop.

.EXAMPLES
    ./scripts/benchmark.ps1
    ./scripts/benchmark.ps1 -Mode roslyn -Iterations 20
    ./scripts/benchmark.ps1 -Generate 400 -Iterations 10 -Cold 5
    ./scripts/benchmark.ps1 -Path samples/SampleShop -NoCold
#>
[CmdletBinding()]
param(
    [string]$Path = "samples/SampleShop",

    [ValidateRange(0, 100000)]
    [int]$Generate = 0,

    [ValidateSet("text", "roslyn", "both")]
    [string]$Mode = "both",

    [ValidateRange(0, 1000)]
    [int]$Warmup = 3,

    [ValidateRange(1, 10000)]
    [int]$Iterations = 10,

    [ValidateRange(0, 100)]
    [int]$Cold = 3,

    [switch]$NoCold,

    [switch]$Force,

    [string]$OutputDirectory = "artifacts/bench/results"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$benchmarkProject = Join-Path $repoRoot "benchmarks/CqrsLens.Benchmarks/CqrsLens.Benchmarks.csproj"
$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $repoRoot $OutputDirectory
}

function Invoke-Harness {
    param([string[]]$HarnessArgs)

    # Windows PowerShell turns native stderr into ErrorRecords; keep that local to
    # this call so a harness diagnostic cannot terminate the whole script.
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & dotnet run --project $benchmarkProject -c Release --no-build -- @HarnessArgs 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    if ($exitCode -ne 0) {
        throw "Benchmark harness failed ($exitCode):`n$output"
    }

    return $output
}

function Convert-RunOutput {
    param([string]$Output, [string]$Description)

    try {
        $parsed = $Output | ConvertFrom-Json
        return $parsed
    }
    catch {
        throw "Could not parse $Description benchmark JSON. Raw output:`n$Output"
    }
}

Push-Location $repoRoot
try {
    Write-Host "Building benchmark harness..."
    & dotnet build $benchmarkProject -c Release --nologo -v:q | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Benchmark harness build failed ($LASTEXITCODE)."
    }

    if ($Generate -gt 0) {
        $generateArgs = @("--generate", $Generate, "--generate-only")
        if ($Force) {
            $generateArgs += "--force"
        }
        Invoke-Harness -HarnessArgs $generateArgs | Out-Null
        $Path = "artifacts/bench/synth-$Generate"
    }

    if (-not (Test-Path $Path)) {
        throw "Benchmark path does not exist: $Path"
    }

    $resolvedPath = (Resolve-Path $Path).Path
    Write-Host ""
    Write-Host "Warm analysis: $Warmup warmup + $Iterations measured iteration(s)"
    $warmOutput = Invoke-Harness -HarnessArgs @(
        $resolvedPath, "--mode", $Mode, "--warmup", $Warmup, "--iterations", $Iterations, "--json"
    )
    $warmRuns = @(Convert-RunOutput -Output $warmOutput -Description "warm")

    foreach ($run in $warmRuns) {
        Write-Host ("  {0,-7} min {1,8:0.00} ms  median {2,8:0.00} ms  p95 {3,8:0.00} ms  alloc {4,7:0.00} MB" -f `
            $run.mode, `
            [double]$run.milliseconds.Min, `
            [double]$run.milliseconds.Median, `
            [double]$run.milliseconds.P95, `
            ([double]$run.allocatedBytes.Median / 1048576.0))
    }

    $coldRuns = @()
    if (-not $NoCold -and $Cold -gt 0) {
        Write-Host ""
        Write-Host "Cold process: $Cold fresh process(es) per mode"
        $modes = if ($Mode -eq "both") { @("text", "roslyn") } else { @($Mode) }

        foreach ($coldMode in $modes) {
            for ($sample = 1; $sample -le $Cold; $sample++) {
                $coldOutput = Invoke-Harness -HarnessArgs @(
                    $resolvedPath, "--mode", $coldMode, "--warmup", 0, "--iterations", 1, "--json"
                )
                $runs = @(Convert-RunOutput -Output $coldOutput -Description "cold")
                if ($runs.Count -ne 1) {
                    throw "Expected one cold run for $coldMode, got $($runs.Count)."
                }

                $run = $runs[0]
                $coldRuns += [pscustomobject]@{
                    mode = $coldMode
                    sample = $sample
                    milliseconds = [double]$run.milliseconds.Median
                    allocatedBytes = [long]$run.allocatedBytes.Median
                    gen0Collections = [double]$run.gen0Collections.Median
                }
                Write-Host ("  {0,-7} sample {1,2} {2,8:0.00} ms  alloc {3,7:0.00} MB" -f `
                    $coldMode, $sample, [double]$run.milliseconds.Median,
                    ([double]$run.allocatedBytes.Median / 1048576.0))
            }
        }
    }

    New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $outputFile = Join-Path $outputRoot "benchmark-$stamp.json"
    $payload = [ordered]@{
        generatedAt = (Get-Date).ToString("o")
        path = $resolvedPath
        mode = $Mode
        warmup = $Warmup
        iterations = $Iterations
        warm = $warmRuns
        cold = $coldRuns
    }
    $jsonText = $payload | ConvertTo-Json -Depth 20
    [System.IO.File]::WriteAllText($outputFile, $jsonText, (New-Object System.Text.UTF8Encoding($false)))

    Write-Host ""
    Write-Host "Saved $outputFile"
}
finally {
    Pop-Location
}
