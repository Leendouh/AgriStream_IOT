<#
.SYNOPSIS
    Load-test the AgriStream App Service - simulates concurrent users hitting the site.

.DESCRIPTION
    Fires $TotalRequests HTTP requests at $BaseUrl with up to $Concurrency parallel
    workers, then reports throughput + latency stats. Run during the demo to drive
    CPU up so the App Service auto-scale rule can be observed in Azure Monitor.

.EXAMPLE
    .\LoadTest.ps1 -BaseUrl "https://st123456.azurewebsites.net" -TotalRequests 2000 -Concurrency 40
#>
param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [int]$TotalRequests = 1000,
    [int]$Concurrency = 25,
    [string]$Path = "/"
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd('/')

Write-Host "Load testing $BaseUrl$Path - $TotalRequests requests, $Concurrency concurrent..." -ForegroundColor Cyan

# Each worker measures its own share and returns result objects via the pipeline.
$scriptBlock = {
    param($Url, $Count)
    Add-Type -AssemblyName System.Net.Http   # needed in PS 5.1 job runspaces
    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromSeconds(60)
    for ($i = 0; $i -lt $Count; $i++) {
        $t = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            $resp = $client.GetAsync($Url).Result
            $t.Stop()
            [pscustomobject]@{
                Ms  = $t.Elapsed.TotalMilliseconds
                Err = if ($resp.IsSuccessStatusCode) { $null } else { "HTTP $([int]$resp.StatusCode)" }
            }
        } catch {
            $t.Stop()
            [pscustomobject]@{ Ms = $t.Elapsed.TotalMilliseconds; Err = $_.Exception.Message }
        }
    }
    $client.Dispose()
}

$perWorker = [Math]::Ceiling($TotalRequests / $Concurrency)
$remaining = $TotalRequests
$jobs = for ($w = 0; $w -lt $Concurrency -and $remaining -gt 0; $w++) {
    $take = [Math]::Min($perWorker, $remaining)
    $remaining -= $take
    Start-Job -ScriptBlock $scriptBlock -ArgumentList "$BaseUrl$Path", $take
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
while (($jobs | Where-Object State -eq 'Running').Count -gt 0) {
    $done = ($jobs | Where-Object State -ne 'Running').Count
    Write-Progress -Activity "Load test running" -Status "$done/$($jobs.Count) workers finished" `
        -PercentComplete (100 * $done / $jobs.Count)
    Start-Sleep -Seconds 3
}
$sw.Stop()
Write-Progress -Activity "Load test running" -Completed

$data = @($jobs | Receive-Job)
$jobs | Remove-Job

$ok    = @($data | Where-Object { $null -eq $_.Err })
$times = @($ok.Ms | Sort-Object)
$fail  = @($data | Where-Object { $null -ne $_.Err })

Write-Host ""
Write-Host "================ LOAD TEST RESULTS ================" -ForegroundColor Green
Write-Host ("Target            : {0}{1}" -f $BaseUrl, $Path)
Write-Host ("Duration          : {0:N1}s" -f $sw.Elapsed.TotalSeconds)
Write-Host ("Successful        : {0} / {1}" -f $ok.Count, $TotalRequests)
Write-Host ("Failed            : {0}" -f $fail.Count)
if ($sw.Elapsed.TotalSeconds -gt 0) {
    Write-Host ("Throughput        : {0:N1} req/s" -f ($ok.Count / $sw.Elapsed.TotalSeconds))
}
if ($times.Count -gt 0) {
    Write-Host ("Avg latency       : {0:N0} ms" -f ($times | Measure-Object -Average).Average)
    Write-Host ("p50 / p95 / max   : {0:N0} / {1:N0} / {2:N0} ms" -f `
        $times[[Math]::Floor($times.Count * 0.50)], `
        $times[[Math]::Floor($times.Count * 0.95)], `
        $times[-1])
}
if ($fail.Count -gt 0) {
    Write-Host "Failure samples:" -ForegroundColor Yellow
    $fail | Select-Object -First 5 | ForEach-Object { Write-Host "  $($_.Err)" }
}
Write-Host "===================================================="
Write-Host "TIP: Watch App Service > Metrics (CPU %) and autoscale Run history in the Azure portal while this runs."
