# Windows'un medya oturumlarını ve çalıp çalmadıklarını yazar (SMTC). measure.ps1'den önce ve sonra
# çalıştır: "boşta" ölçümü ancak hiçbir şey çalmıyorken boşta sayılır.
#   powershell -ExecutionPolicy Bypass -File scripts\media-state.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType = WindowsRuntime]

$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and
    $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
} | Select-Object -First 1
$request = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()
$task = $asTask.MakeGenericMethod([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]).Invoke($null, @($request))
$null = $task.Wait(5000)

$sessions = @($task.Result.GetSessions() | ForEach-Object { '{0}={1}' -f $_.SourceAppUserModelId, $_.GetPlaybackInfo().PlaybackStatus })
if ($sessions.Count -eq 0) { 'medya: oturum yok' } else { 'medya: ' + ($sessions -join ', ') }
