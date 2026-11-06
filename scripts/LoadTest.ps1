<#
.SYNOPSIS
    Load-test the AgriStream App Service - simulates concurrent users hitting the site.

.DESCRIPTION
    Fires $TotalRequests HTTP requests at $BaseUrl with up to $Concurrency parallel
    workers, then reports throughput + latency stats. Run during the demo to drive
    CPU up so the App Service auto-scale rule can be observed in Azure Monitor.

.EXAMPLE
    .\LoadTest.ps1 -BaseUrl "https://agristream-st123456.azurewebsites.net" -TotalRequests 2000 -Concurrency 40
#>
param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [int]$TotalRequests = 1000,
    [int]$Concurrency = 25,
    [string]$Path = "/"
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd('/')
$sw = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host "Load testing $BaseUrl$Path - $TotalRequests requests, $Concurrency concurrent..." -ForegroundColor Cyan

# Shared result buckets (thread-safe)
$results  = [System.Collections.Concurrent.ConcurrentBag[double]]::new()
$failures = [System.Collections.Concurrent.ConcurrentBag[string]]::new()
$remaining = $TotalRequests

$scriptBlock = {
    param($Url, $Count, $Results, $Failures)
    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromSeconds(60)
    for ($i = 0; $i -lt $Count; $i++) {
        $t = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            $resp = $client.GetAsync($Url).Result
            $t.Stop()
            if ($resp.IsSuccessStatusCode) { $Results.Add($t.Elapsed.TotalMilliseconds) }
            else { $Failures.Add("HTTP $([int]$resp.StatusCode)") }
        } catch {
            $t.Stop()
            $Failures.Add($_.Exception.InnerException?.Message ?? $_.Exception.Message)
        }
    }
    $client.Dispose()
}

# Split work across jobs (batches of Concurrency workers)
$perWorker = [Math]::Ceiling($TotalRequests / $Concurrency)
$jobs = @()
for ($w = 0; $w -lt $Concurrency -and $remaining -gt 0; $w++) {
    $take = [Math]::Min($perWorker, $remaining)
    $remaining -= $take
    $jobs += Start-Job -ScriptBlock $scriptBlock -ArgumentList "$BaseUrl$Path", $take, $results, $failures
}

$jobs | Wait-Job | Out-Null
$sw.Stop()
$jobs | Receive-Job | Out-Null   # surface any worker errors
$jobs | Remove-Job

$ok = $results.Count
$times = @($results | Sort-Object)
if ($ok -gt 0) {
    $avg = ($times | Measure-Object -Average).Average
    $p50 = $times[[Math]::Floor($ok * 0.50)]
    $p95 = $times[[Math]::Floor($ok * 0.95)]
    $max = $times[-1]
}

Write-Host ""
Write-Host "================ LOAD TEST RESULTS ================" -ForegroundColor Green
Write-Host ("Target            : {0}{1}" -f $BaseUrl, $Path)
Write-Host ("Duration          : {0:N1}s" -f $sw.Elapsed.TotalSeconds)
Write-Host ("Successful        : {0} / {1}" -f $ok, $TotalRequests)
Write-Host ("Failed            : {0}" -f $failures.Count)
Write-Host ("Throughput        : {0:N1} req/s" -f ($ok / $sw.Elapsed.TotalSeconds))
if ($ok -gt 0) {
    Write-Host ("Avg latency       : {0:N0} ms" -f $avg)
    Write-Host ("p50 / p95 / max   : {0:N0} / {1:N0} / {2:N0} ms" -f $p50, $p95, $max)
}
if ($failures.Count -gt 0) {
    Write-Host "Failure samples:" -ForegroundColor Yellow
    $failures | Select-Object -First 5 | ForEach-Object { Write-Host "  $_" }
}
Write-Host "===================================================="
Write-Host "TIP: Watch App Service > Metrics (CPU %) and your autoscale history in the Azure portal while this runs."
