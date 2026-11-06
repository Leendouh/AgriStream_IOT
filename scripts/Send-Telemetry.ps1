<#
.SYNOPSIS
    Pushes simulated soil-sensor readings into AgriStream via POST /api/telemetry.

.EXAMPLE
    .\Send-Telemetry.ps1 -BaseUrl "https://agristream-st123456.azurewebsites.net" -Count 200
#>
param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [int]$Count = 50,
    [int]$DelayMs = 100
)

$BaseUrl = $BaseUrl.TrimEnd('/')
$devices = @("SENSOR-A1", "SENSOR-B2", "SENSOR-C3", "SENSOR-D4")
$rnd = [Random]::new()

for ($i = 1; $i -le $Count; $i++) {
    $body = @{
        deviceId     = $devices[$rnd.Next($devices.Count)]
        soilMoisture = [Math]::Round(15 + $rnd.NextDouble() * 50, 1)
        temperatureC = [Math]::Round(10 + $rnd.NextDouble() * 25, 1)
        humidity     = [Math]::Round(30 + $rnd.NextDouble() * 60, 1)
        batteryLevel = [Math]::Round(60 + $rnd.NextDouble() * 40, 0)
    } | ConvertTo-Json

    try {
        Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/telemetry" -Body $body -ContentType "application/json" | Out-Null
        Write-Host "[$i/$Count] sent reading for $($body | ConvertFrom-Json | Select-Object -Expand deviceId)"
    } catch {
        Write-Warning "[$i/$Count] failed: $($_.Exception.Message)"
    }
    Start-Sleep -Milliseconds $DelayMs
}
Write-Host "Done. Open the Dashboard or Telemetry page to see the readings." -ForegroundColor Green
