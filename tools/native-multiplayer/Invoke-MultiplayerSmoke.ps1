[CmdletBinding()]
param(
    [switch]$Launch,
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley',
    [int]$TimeoutSeconds = 180,
    [string[]]$ExtraMods = @(),
    [switch]$Wardrobe
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$gameExe = Join-Path $GamePath 'StardewModdingAPI.exe'
if (!(Test-Path -LiteralPath $gameExe)) { throw "Missing StardewModdingAPI.exe: $gameExe" }

function Assert-NoGameProcess {
    $existing = @(Get-Process | Where-Object { $_.ProcessName -match '^(Stardew(?: Valley)?|StardewModdingAPI)$' })
    if ($existing.Count -gt 0) { throw "Refusing to launch while game processes exist: $($existing.Id -join ', ')" }
}

function Copy-ExtraMods([string]$modsRoot) {
    foreach ($source in $ExtraMods) {
        if (!(Test-Path -LiteralPath $source -PathType Container)) { throw "Extra mod directory not found: $source" }
        $name = Split-Path $source -Leaf
        Copy-Item -LiteralPath $source -Destination (Join-Path $modsRoot $name) -Recurse
    }
}

function Wait-State([string]$driverDir, [scriptblock]$predicate, [string]$label) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $failed = Join-Path $driverDir 'failed.json'
        if (Test-Path $failed) { throw "$label driver failed: $(Get-Content $failed -Raw)" }
        $statePath = Join-Path $driverDir 'state.json'
        if (Test-Path $statePath) {
            $state = Get-Content $statePath -Raw | ConvertFrom-Json
            if (& $predicate $state) { return $state }
        }
        Start-Sleep -Milliseconds 400
    } while ((Get-Date) -lt $deadline)
    throw "$label timed out"
}

function Wait-Stable-State([string]$driverDir, [scriptblock]$check, [string]$label) {
    $free = Wait-State $driverDir { param($s) $s.PlayerFree -and !$s.Event -and !$s.EventUp } "$label free"
    $after = $free.Tick + 120
    Wait-State $driverDir { param($s) $s.Tick -ge $after -and (& $check $s) } $label
}

function Assert-PeerState($state, $baseline, [string]$label) {
    if (!$state.WorldReady -or !$state.PlayerFree -or $state.Event -or $state.EventUp -or $state.ClientTimedOut) {
        throw "$label was not responsive and event-free."
    }
    foreach ($field in @('Hidden', 'Hat', 'Hair', 'Shirt', 'Pants')) {
        if ($state.$field -ne $baseline.$field) { throw "$label changed $field from the connected baseline." }
    }
}

function Request([string]$driverDir, [string]$operation) {
    $id = [guid]::NewGuid().ToString('N')
    $requests = Join-Path $driverDir 'requests'
    $responses = Join-Path $driverDir 'responses'
    New-Item -ItemType Directory -Force $requests, $responses | Out-Null
    $temporary = Join-Path $requests "$id.tmp"
    $request = @{ Op = $operation } | ConvertTo-Json -Compress
    Set-Content -LiteralPath $temporary -Value $request -Encoding UTF8
    Move-Item -LiteralPath $temporary -Destination (Join-Path $requests "$id.json")
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $responsePath = Join-Path $responses "$id.json"
    do { Start-Sleep -Milliseconds 400 } while (!(Test-Path $responsePath) -and (Get-Date) -lt $deadline)
    if (!(Test-Path $responsePath)) { throw "Request '$operation' timed out in $driverDir" }
    $response = Get-Content $responsePath -Raw | ConvertFrom-Json
    if (!$response.Ok) { throw "Request '$operation' failed: $($response.Error)" }
    return $response
}

function Backup-Logs([string]$destination) {
    $logRoot = Join-Path $env:APPDATA 'StardewValley\ErrorLogs'
    New-Item -ItemType Directory -Force $destination | Out-Null
    if (Test-Path $logRoot) {
        Get-ChildItem $logRoot -File | ForEach-Object { Copy-Item $_.FullName (Join-Path $destination $_.Name) -Force }
    }
}

function Restore-Logs([string]$source) {
    $logRoot = Join-Path $env:APPDATA 'StardewValley\ErrorLogs'
    New-Item -ItemType Directory -Force $logRoot | Out-Null
    if (Test-Path $source) {
        $previous = @(Get-ChildItem $source -File | ForEach-Object Name)
        Get-ChildItem $logRoot -File | Where-Object { $previous -notcontains $_.Name } | Remove-Item -Force
        Get-ChildItem $source -File | ForEach-Object { Copy-Item $_.FullName (Join-Path $logRoot $_.Name) -Force }
    }
}

function Snapshot-Logs([string]$source, [string]$destination) {
    New-Item -ItemType Directory -Force $destination | Out-Null
    if (Test-Path $source) { Get-ChildItem $source -File | ForEach-Object { Copy-Item $_.FullName (Join-Path $destination $_.Name) -Force } }
}

Assert-NoGameProcess
$driver = Join-Path $root 'tools\native-multiplayer\Driver\bin\Release\net6.0'
$bench = Join-Path $root 'benches\SdcsBench.NoCode\export\Bench.SdcsNoCode'
if (!(Test-Path (Join-Path $driver 'NativeMultiplayerDriver.dll'))) { throw 'Build the native driver first.' }
if (!(Test-Path $bench)) { throw 'Export Bench.SdcsNoCode first.' }

$run = Join-Path $root ('.superpowers\sdd\2026-10-24-m9\native-multiplayer\run-' + [guid]::NewGuid().ToString('N'))
$hostMods = Join-Path $run 'host\Mods'; $clientMods = Join-Path $run 'client\Mods'
$hostDriver = Join-Path $hostMods 'NativeMultiplayerDriver'; $clientDriver = Join-Path $clientMods 'NativeMultiplayerDriver'
$logBackup = Join-Path $run 'logs-before'
New-Item -ItemType Directory -Force $hostDriver, $clientDriver, (Join-Path $run 'host'), (Join-Path $run 'client') | Out-Null

foreach ($role in @(@($hostMods, $hostDriver, 'host'), @($clientMods, $clientDriver, 'client'))) {
    $mods = $role[0]; $destination = $role[1]; $name = $role[2]
    Copy-Item (Join-Path $driver 'NativeMultiplayerDriver.dll'), (Join-Path $driver 'NativeMultiplayerDriver.deps.json') $destination
    Copy-Item -LiteralPath (Join-Path $root 'tools/native-multiplayer/Driver/manifest.json') -Destination (Join-Path $destination 'manifest.json')
    @{ Role=$name } | ConvertTo-Json | Set-Content (Join-Path $destination 'role.json') -Encoding UTF8
    Copy-Item $bench (Join-Path $mods 'Bench.SdcsNoCode') -Recurse
    if ($Wardrobe) {
        $overlay = Join-Path $root 'tools/native-compat/SceneOverlay'
        foreach ($file in Get-ChildItem -LiteralPath $overlay -Recurse -File) {
            $relative = $file.FullName.Substring($overlay.Length + 1)
            $target = Join-Path (Join-Path $mods 'Bench.SdcsNoCode') $relative
            New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        }
    }
    Copy-ExtraMods $mods
}
@{ Run=$run; Created=(Get-Date).ToString('o'); HostMods=$hostMods; ClientMods=$clientMods; ExtraMods=$ExtraMods } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'run.json') -Encoding UTF8

if (!$Launch) { Write-Host "Prepared $run"; return }

Backup-Logs $logBackup
$hostProcess = $null; $clientProcess = $null
try {
    Assert-NoGameProcess
    $hostProcess = Start-Process -FilePath $gameExe -ArgumentList @('--mods-path', "`"$hostMods`"", '--no-terminal') -WorkingDirectory $GamePath -WindowStyle Hidden -PassThru
    $hostProcess.Id | Set-Content (Join-Path $run 'host\pid.txt')
    $hostBaseline = Wait-State $hostDriver { param($s) $s.WorldReady -and $s.IsServer } 'host'

    $clientProcess = Start-Process -FilePath $gameExe -ArgumentList @('--mods-path', "`"$clientMods`"", '--no-terminal') -WorkingDirectory $GamePath -WindowStyle Hidden -PassThru
    $clientProcess.Id | Set-Content (Join-Path $run 'client\pid.txt')
    $clientBaseline = Wait-State $clientDriver { param($s) $s.WorldReady -and $s.IsClient -and $s.OtherFarmers.Count -gt 0 -and !$s.ClientTimedOut } 'client'
    $hostBaseline = Request $hostDriver 'state' | Select-Object -ExpandProperty Data
    $clientBaseline = Request $clientDriver 'state' | Select-Object -ExpandProperty Data
    if ($Wardrobe) {
        $wardrobeResult = @{ Host = (Request $hostDriver 'wardrobe'); Client = (Request $clientDriver 'wardrobe') }
        if (!$wardrobeResult.Host.Data.Applied -or !$wardrobeResult.Client.Data.Applied) { throw 'Wardrobe fixture was not applied on both peers.' }
        $wardrobeResult | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $run 'wardrobe.json') -Encoding UTF8
        $hostBaseline = (Request $hostDriver 'state').Data
        $clientBaseline = (Request $clientDriver 'state').Data
    }
    if (!($hostBaseline.OtherFarmers.Id -contains $clientBaseline.PlayerId)) { throw 'Host did not report the connected client farmer.' }
    if (!($clientBaseline.OtherFarmers.Id -contains $hostBaseline.PlayerId)) { throw 'Client did not report the connected host farmer.' }
    if (!$hostBaseline.WorldReady -or !$hostBaseline.PlayerFree -or $hostBaseline.Event -or $hostBaseline.EventUp -or $hostBaseline.ClientTimedOut) { throw 'Host was not responsive at the post-connect baseline.' }
    if (!$clientBaseline.WorldReady -or !$clientBaseline.PlayerFree -or $clientBaseline.Event -or $clientBaseline.EventUp -or $clientBaseline.ClientTimedOut) { throw 'Client was not responsive at the post-connect baseline.' }
    @{ Host=$hostBaseline; Client=$clientBaseline } | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $run 'connected.json')

    $hostPlay = Request $hostDriver 'play'
    if (!$hostPlay.Data.Accepted) { throw 'Host play was not accepted.' }
    $hostPlaying = Wait-State $hostDriver { param($s) $s.Event -eq 'Bench.SdcsNoCode.wave' -and !$s.PlayerFree } 'host play'
    $hostPlaying = Wait-State $hostDriver { param($s) $s.Event -eq 'Bench.SdcsNoCode.wave' -and $s.DialogueUp } 'host clip and pose reach dialogue'
    $clientDuringHost = Get-Content (Join-Path $clientDriver 'state.json') -Raw | ConvertFrom-Json
    Assert-PeerState $clientDuringHost $clientBaseline 'client during host event'
    $hostStop = Request $hostDriver 'stop'
    $hostStopped = Wait-Stable-State $hostDriver { param($s) $s.Hidden -eq $hostBaseline.Hidden -and $s.Hat -eq $hostBaseline.Hat -and $s.Shirt -eq $hostBaseline.Shirt -and $s.Pants -eq $hostBaseline.Pants -and $s.Hair -eq $hostBaseline.Hair } 'host stop'
    if ($hostStopped.Hidden -ne $hostBaseline.Hidden) { throw "Host hidden state was not restored: baseline=$($hostBaseline.Hidden), final=$($hostStopped.Hidden)" }

    $clientPlay = Request $clientDriver 'play'
    if (!$clientPlay.Data.Accepted) { throw 'Client play was not accepted.' }
    $clientPlaying = Wait-State $clientDriver { param($s) $s.Event -eq 'Bench.SdcsNoCode.wave' -and !$s.PlayerFree } 'client play'
    $clientPlaying = Wait-State $clientDriver { param($s) $s.Event -eq 'Bench.SdcsNoCode.wave' -and $s.DialogueUp } 'client clip and pose reach dialogue'
    $hostDuringClient = Get-Content (Join-Path $hostDriver 'state.json') -Raw | ConvertFrom-Json
    Assert-PeerState $hostDuringClient $hostBaseline 'host during client event'
    $clientStop = Request $clientDriver 'stop'
    $clientStopped = Wait-Stable-State $clientDriver { param($s) $s.Hidden -eq $clientBaseline.Hidden -and $s.Hat -eq $clientBaseline.Hat -and $s.Shirt -eq $clientBaseline.Shirt -and $s.Pants -eq $clientBaseline.Pants -and $s.Hair -eq $clientBaseline.Hair } 'client stop'
    if ($clientStopped.Hidden -ne $clientBaseline.Hidden) { throw "Client hidden state was not restored: baseline=$($clientBaseline.Hidden), final=$($clientStopped.Hidden)" }

    @{ HostPlay=$hostPlay; HostPlaying=$hostPlaying; ClientDuringHost=$clientDuringHost; HostStop=$hostStop; HostStopped=$hostStopped; ClientPlay=$clientPlay; ClientPlaying=$clientPlaying; HostDuringClient=$hostDuringClient; ClientStop=$clientStop; ClientStopped=$clientStopped } |
        ConvertTo-Json -Depth 12 | Set-Content (Join-Path $run 'result.json') -Encoding UTF8
    Write-Host "M9.4 PASS: handshake, host/client isolation, bidirectional play/stop, and hidden restoration. Run: $run"
}
catch {
    $_ | Out-String | Set-Content (Join-Path $run 'failed.json') -Encoding UTF8
    throw
}
finally {
    try {
        foreach ($process in @($clientProcess, $hostProcess)) {
            if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit(15000) | Out-Null }
        }
    }
    finally {
        try { Snapshot-Logs (Join-Path $env:APPDATA 'StardewValley\ErrorLogs') (Join-Path $run 'logs-after') }
        finally { Restore-Logs $logBackup }
    }
}
