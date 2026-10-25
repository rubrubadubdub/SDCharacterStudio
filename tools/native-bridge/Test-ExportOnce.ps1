# The Bridge's automatic export happens once, ever (D-47). Starts an isolated game twice with only the Runtime and the
# Bridge, a private export folder and a private pipe:
#   launch 1: no export yet -> the Bridge exports once; status pings are answered while it works; it completes.
#   launch 2: the export exists -> nothing is exported.
# Never starts while another Stardew Valley or SMAPI process runs. The owner's SMAPI-latest.txt is restored afterwards.
param([int]$ExportTimeoutSeconds = 600)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$game = 'C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley'
$run = Join-Path $repo 'benches\.run\bridge-once'
$log = Join-Path $env:APPDATA 'StardewValley\ErrorLogs\SMAPI-latest.txt'
$pipe = 'SDCS.BridgeOnce.' + [Guid]::NewGuid().ToString('N')

function Assert-NoGame {
    $running = Get-Process | Where-Object { $_.ProcessName -match 'Stardew|StardewModdingAPI' }
    if ($running) { throw "A game process is already running ($($running.Id -join ', ')). Not starting another." }
}
function Read-Log { if (Test-Path $log) { $s = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite'); try { (New-Object IO.StreamReader($s)).ReadToEnd() } finally { $s.Dispose() } } else { '' } }
function Ping {
    $client = New-Object IO.Pipes.NamedPipeClientStream('.', $pipe, [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $client.Connect(1000)
        $writer = New-Object IO.StreamWriter($client); $writer.AutoFlush = $true
        $reader = New-Object IO.StreamReader($client)
        $writer.WriteLine('{"Op":"Ping","Id":"check"}')
        $line = $reader.ReadLineAsync()
        if ($line.Wait(2000)) { return $line.Result } else { return 'TIMEOUT' }
    }
    catch { return 'NO PIPE' }
    finally { $client.Dispose() }
}

Assert-NoGame
foreach ($project in 'Runtime', 'Bridge') {
    & dotnet build (Join-Path $repo "src\SDCharacterStudio.$project\SDCharacterStudio.$project.csproj") -c Release -v q -nologo -p:EnableModDeploy=false | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}
if (Test-Path $run) { Remove-Item -LiteralPath $run -Recurse -Force }
$mods = Join-Path $run 'Mods'
$runtime = Join-Path $mods 'SDCharacterStudio'; $bridge = Join-Path $mods 'SDCharacterStudio.Bridge'
New-Item -ItemType Directory -Path $runtime, $bridge -Force | Out-Null
$src = Join-Path $repo 'src'
Copy-Item "$src\SDCharacterStudio.Runtime\bin\Release\net6.0\SDCharacterStudio.Runtime.dll", "$src\SDCharacterStudio.Runtime\bin\Release\net6.0\SDCharacterStudio.Core.dll", "$src\SDCharacterStudio.Runtime\manifest.json" $runtime
Copy-Item "$src\SDCharacterStudio.Runtime\i18n" $runtime -Recurse
Copy-Item "$src\SDCharacterStudio.Bridge\bin\Release\net6.0\SDCharacterStudio.Bridge.dll", "$src\SDCharacterStudio.Runtime\bin\Release\net6.0\SDCharacterStudio.Core.dll", "$src\SDCharacterStudio.Bridge\manifest.json" $bridge
Copy-Item "$src\SDCharacterStudio.Bridge\assets" $bridge -Recurse
@{ ExportFolder = (Join-Path $run 'Export'); Profile = 'default'; AutoExportOnSaveLoaded = $true; ExportLocations = $true
   PreviewPipeName = $pipe; PreviewRequestFolder = (Join-Path $run 'requests') } | ConvertTo-Json | Set-Content (Join-Path $bridge 'config.json') -Encoding UTF8

$ownerLog = Join-Path $run 'owner-SMAPI-latest.txt'
if (Test-Path $log) { Copy-Item $log $ownerLog -Force }
$results = [ordered]@{}
try {
    foreach ($launch in 1, 2) {
        Assert-NoGame
        if (Test-Path $log) { Remove-Item -LiteralPath $log -Force }   # read only this launch's log (the owner's is backed up)
        $process = Start-Process -FilePath (Join-Path $game 'StardewModdingAPI.exe') -ArgumentList @('--mods-path', ('"' + $mods + '"'), '--no-terminal') -WorkingDirectory $game -WindowStyle Hidden -PassThru
        Write-Output "Launch $($launch): own isolated game, PID $($process.Id)"
        try {
            $deadline = (Get-Date).AddSeconds($ExportTimeoutSeconds)
            $pings = @()
            while ($true) {
                if ($process.HasExited) { throw "The game exited during launch $launch." }
                if ((Get-Date) -gt $deadline) { throw "Launch $($launch): timed out." }
                $text = Read-Log
                if ($text -match 'Exporting this game') { $p = Ping; if ($p -ne 'NO PIPE') { $pings += $p } }
                if ($text -match 'Export completed: (\d+) jobs') { break }
                if ($text -match 'not exporting again') { break }
                Start-Sleep -Milliseconds 700
            }
            Start-Sleep -Seconds 2
            $text = Read-Log
            $results["launch$launch"] = [ordered]@{
                Exported = [bool]($text -match 'Exporting this game'); Completed = [bool]($text -match 'Export completed')
                Skipped = [bool]($text -match 'not exporting again'); Errors = @([regex]::Matches($text, '(?m)^\[[0-9:]+ ERROR[^\]]*\].*$') | ForEach-Object Value | Where-Object { $_ -notmatch 'Steam achievements' })
                PingsWhileExporting = $pings.Count; PingTimeouts = @($pings | Where-Object { $_ -eq 'TIMEOUT' }).Count
                SamplePing = ($pings | Where-Object { $_ -ne 'TIMEOUT' } | Select-Object -First 1)
            }
            $text | Set-Content (Join-Path $run "SMAPI-launch$launch.txt") -Encoding UTF8
        }
        finally {
            if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit(15000) | Out-Null }
            Write-Output "Stopped own game PID $($process.Id)"
        }
    }
}
finally {
    if (Test-Path $ownerLog) { Copy-Item $ownerLog $log -Force }
}
$results | ConvertTo-Json -Depth 4 | Tee-Object -FilePath (Join-Path $run 'result.json')
$one = $results['launch1']; $two = $results['launch2']
$ok = $one.Exported -and $one.Completed -and $one.Errors.Count -eq 0 -and $one.PingTimeouts -eq 0 -and $one.PingsWhileExporting -gt 0 -and
      $two.Skipped -and -not $two.Exported -and $two.Errors.Count -eq 0 -and (Test-Path (Join-Path $run 'Export\default\catalog.json'))
if (-not $ok) { throw 'Export-once check failed; see result.json.' }
Write-Output 'Export-once check passed.'
