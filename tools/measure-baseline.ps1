# Échantillonne CPU / GPU / RAM du process wallflow pendant N secondes et ajoute
# une ligne agrégée à baseline-results.csv. Le scénario (quel fichier joue, pause,
# fenêtre ouverte…) se met en place AVANT d'appeler le script — il ne pilote pas l'app.
#
# Usage : .\measure-baseline.ps1 -Scenario "webp-lecture" -Run 1 [-Seconds 60] [-NoGpu]
#         [-SecondProcessName ffmpeg] [-Media "chemin, taille, durée"] [-Commit abc1234]
# -NoGpu : saute la collecte Get-Counter (contrôle négatif du coût de la sonde GPU).
# -SecondProcessName : échantillonne aussi ce process (ex. conversion-ffmpeg) ; absent
#   tant qu'il n'a pas démarré ou après sa fin, les échantillons manquants sont ignorés.
# Rejouer après chaque optimisation pour prouver le gain (comparer les lignes du CSV).
param(
    [Parameter(Mandatory)][string]$Scenario,
    [Parameter(Mandatory)][int]$Run,
    [int]$Seconds = 60,
    [string]$ProcessName = "wallflow",
    [switch]$NoGpu,
    [string]$SecondProcessName = "",
    [string]$Media = "",
    [string]$Commit = "",
    [string]$OutFile = "$PSScriptRoot\baseline-results.csv"
)

if (-not $Commit) {
    $Commit = (git -C $PSScriptRoot rev-parse --short HEAD 2>$null)
}

$proc = Get-Process $ProcessName -ErrorAction Stop
$cores = [Environment]::ProcessorCount
$samples = New-Object System.Collections.Generic.List[object]
$secondSamples = New-Object System.Collections.Generic.List[object]

$prevCpu = $proc.TotalProcessorTime
$prevT = Get-Date
$secondPrev = $null

$clock = [Diagnostics.Stopwatch]::StartNew()
while ($clock.Elapsed.TotalSeconds -lt $Seconds) {
    Start-Sleep -Seconds 1
    $proc.Refresh()
    if ($proc.HasExited) { throw "$ProcessName s'est arrêté pendant la mesure" }

    $now = Get-Date
    # % CPU machine entière (même convention que le Gestionnaire des tâches).
    $cpu = ($proc.TotalProcessorTime - $prevCpu).TotalMilliseconds / ($now - $prevT).TotalMilliseconds / $cores * 100
    $prevCpu = $proc.TotalProcessorTime

    $gpu = $null
    if (-not $NoGpu) {
        try {
            $g = (Get-Counter "\GPU Engine(pid_$($proc.Id)_*)\Utilization Percentage" -ErrorAction Stop).CounterSamples |
                Measure-Object -Property CookedValue -Sum
            $gpu = $g.Sum
        } catch {}
    }

    $samples.Add([pscustomobject]@{ Cpu = $cpu; Gpu = $gpu; RamMB = $proc.WorkingSet64 / 1MB })

    if ($SecondProcessName) {
        $sp = Get-Process $SecondProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($sp) {
            if ($secondPrev -and $secondPrev.Id -eq $sp.Id) {
                $spCpu = ($sp.TotalProcessorTime - $secondPrev.TotalProcessorTime).TotalMilliseconds / ($now - $prevT).TotalMilliseconds / $cores * 100
                $secondSamples.Add([pscustomobject]@{ Cpu = $spCpu; RamMB = $sp.WorkingSet64 / 1MB })
            }
            $secondPrev = [pscustomobject]@{ Id = $sp.Id; TotalProcessorTime = $sp.TotalProcessorTime }
        } else {
            $secondPrev = $null
        }
    }

    $prevT = $now
}

$cpuStats = $samples | Measure-Object -Property Cpu -Average -Maximum
$ramStats = $samples | Measure-Object -Property RamMB -Average -Maximum
$gpuVals = @($samples | Where-Object { $null -ne $_.Gpu })
$gpuStats = $null
if ($gpuVals.Count -gt 0) { $gpuStats = $gpuVals | Measure-Object -Property Gpu -Average -Maximum }

$secondCpuStats = $null
$secondRamStats = $null
if ($secondSamples.Count -gt 0) {
    $secondCpuStats = $secondSamples | Measure-Object -Property Cpu -Average -Maximum
    $secondRamStats = $secondSamples | Measure-Object -Property RamMB -Average -Maximum
}

$row = [pscustomobject]@{
    Date               = (Get-Date).ToString("yyyy-MM-dd HH:mm")
    Scenario           = $Scenario
    Run                = $Run
    Commit             = $Commit
    Seconds            = [math]::Round($clock.Elapsed.TotalSeconds)
    CpuAvg             = [math]::Round($cpuStats.Average, 2)
    CpuMax             = [math]::Round($cpuStats.Maximum, 2)
    GpuCounters        = if ($NoGpu) { "off" } else { "on" }
    GpuAvg             = if ($gpuStats) { [math]::Round($gpuStats.Average, 2) } else { "" }
    GpuMax             = if ($gpuStats) { [math]::Round($gpuStats.Maximum, 2) } else { "" }
    RamAvgMB           = [math]::Round($ramStats.Average)
    RamMaxMB           = [math]::Round($ramStats.Maximum)
    Media              = $Media
    SecondProcess      = $SecondProcessName
    SecondCpuAvg       = if ($secondCpuStats) { [math]::Round($secondCpuStats.Average, 2) } else { "" }
    SecondCpuMax       = if ($secondCpuStats) { [math]::Round($secondCpuStats.Maximum, 2) } else { "" }
    SecondRamAvgMB     = if ($secondRamStats) { [math]::Round($secondRamStats.Average) } else { "" }
    SecondRamMaxMB     = if ($secondRamStats) { [math]::Round($secondRamStats.Maximum) } else { "" }
}

$writeHeader = -not (Test-Path $OutFile)
$line = $row | ConvertTo-Csv -NoTypeInformation
if ($writeHeader) { $line | Out-File $OutFile -Encoding utf8 }
else { $line | Select-Object -Skip 1 | Out-File $OutFile -Append -Encoding utf8 }

$row | Format-Table | Out-String | Write-Output
