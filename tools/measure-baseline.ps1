# Échantillonne CPU / GPU / RAM du process wallflow pendant N secondes et ajoute
# une ligne agrégée à baseline-results.csv. Le scénario (quel fichier joue, pause,
# fenêtre ouverte…) se met en place AVANT d'appeler le script — il ne pilote pas l'app.
#
# Usage : .\measure-baseline.ps1 -Scenario "webp-lecture" [-Seconds 60]
# Rejouer après chaque optimisation pour prouver le gain (comparer les lignes du CSV).
param(
    [Parameter(Mandatory)][string]$Scenario,
    [int]$Seconds = 60,
    [string]$ProcessName = "wallflow",
    [string]$OutFile = "$PSScriptRoot\baseline-results.csv"
)

$proc = Get-Process $ProcessName -ErrorAction Stop
$cores = [Environment]::ProcessorCount
$samples = New-Object System.Collections.Generic.List[object]

$prevCpu = $proc.TotalProcessorTime
$prevT = Get-Date
$clock = [Diagnostics.Stopwatch]::StartNew()
while ($clock.Elapsed.TotalSeconds -lt $Seconds) {
    Start-Sleep -Seconds 1
    $proc.Refresh()
    if ($proc.HasExited) { throw "$ProcessName s'est arrêté pendant la mesure" }

    $now = Get-Date
    # % CPU machine entière (même convention que le Gestionnaire des tâches).
    $cpu = ($proc.TotalProcessorTime - $prevCpu).TotalMilliseconds / ($now - $prevT).TotalMilliseconds / $cores * 100
    $prevCpu = $proc.TotalProcessorTime
    $prevT = $now

    # Somme des moteurs GPU du process ; compteur parfois absent (session distante, vieux pilote).
    $gpu = $null
    try {
        $g = (Get-Counter "\GPU Engine(pid_$($proc.Id)_*)\Utilization Percentage" -ErrorAction Stop).CounterSamples |
            Measure-Object -Property CookedValue -Sum
        $gpu = $g.Sum
    } catch {}

    $samples.Add([pscustomobject]@{ Cpu = $cpu; Gpu = $gpu; RamMB = $proc.WorkingSet64 / 1MB })
}

$cpuStats = $samples | Measure-Object -Property Cpu -Average -Maximum
$ramStats = $samples | Measure-Object -Property RamMB -Average -Maximum
$gpuVals = @($samples | Where-Object { $null -ne $_.Gpu })
$gpuStats = $null
if ($gpuVals.Count -gt 0) { $gpuStats = $gpuVals | Measure-Object -Property Gpu -Average -Maximum }

$row = [pscustomobject]@{
    Date     = (Get-Date).ToString("yyyy-MM-dd HH:mm")
    Scenario = $Scenario
    Seconds  = [math]::Round($clock.Elapsed.TotalSeconds)
    CpuAvg   = [math]::Round($cpuStats.Average, 2)
    CpuMax   = [math]::Round($cpuStats.Maximum, 2)
    GpuAvg   = if ($gpuStats) { [math]::Round($gpuStats.Average, 2) } else { "" }
    GpuMax   = if ($gpuStats) { [math]::Round($gpuStats.Maximum, 2) } else { "" }
    RamAvgMB = [math]::Round($ramStats.Average)
    RamMaxMB = [math]::Round($ramStats.Maximum)
}

$writeHeader = -not (Test-Path $OutFile)
$line = $row | ConvertTo-Csv -NoTypeInformation
if ($writeHeader) { $line | Out-File $OutFile -Encoding utf8 }
else { $line | Select-Object -Skip 1 | Out-File $OutFile -Append -Encoding utf8 }

$row | Format-Table | Out-String | Write-Output
