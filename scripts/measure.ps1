# ISLe8'in CPU ve bellek kullanımını ölçer (macOS'taki scripts/measure.sh'nin karşılığı).
# Ölçümden önce: ISLe8 en az 30 sn önce açılmış olsun, ada kapalı dursun, fareyle üzerine
# gelme. Müzik çalarken ölçmek için -Label "muzik" gibi bir etiket ver.
#   powershell -ExecutionPolicy Bypass -File scripts\measure.ps1 -Seconds 60
param(
    [int]$Seconds = 60,
    [string]$Label = 'bosta'
)

$ErrorActionPreference = 'Stop'
$process = Get-Process -Name ISLe8 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $process) { throw 'ISLe8 çalışmıyor.' }

$cores = [Environment]::ProcessorCount
$startCpu = $process.TotalProcessorTime
$startTime = Get-Date
Start-Sleep -Seconds $Seconds
$process.Refresh()
$elapsed = ((Get-Date) - $startTime).TotalSeconds
$cpu = ($process.TotalProcessorTime - $startCpu).TotalSeconds

# Görev Yöneticisi gibi: tüm çekirdeklerin toplamına göre yüzde.
$percent = 100 * $cpu / ($elapsed * $cores)
# Görev Yöneticisi'nin "Bellek" sütunu özel çalışma kümesini gösterir. WMI sınıf adları Türkçe
# Windows'ta da değişmez (Get-Counter'ın sayaç adları değişir).
$private = (Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -Filter "IDProcess=$($process.Id)").WorkingSetPrivate

'{0}  {1} sn  CPU %{2:N3}  çalışma kümesi {3:N1} MB  özel {4:N1} MB  iş parçacığı {5}' -f `
    $Label, [int]$elapsed, $percent, ($process.WorkingSet64 / 1MB), ($private / 1MB), $process.Threads.Count
