# Costumes and the camera in the real game (D-49): starts an isolated game (the bench driver's disposable farm in
# ScienceHouse, no save) with only the Runtime, the Bridge and the driver, a private export folder and a private pipe; then
# the editor's native tests run against it:
#   -Suite costume  NativeCostumeTests: the game draws an outfit; a scene with earmuffs keeps them on until it ends.
#   -Suite camera   NativeCameraTests: a scene whose player walks out of view ends with no camera leap on screen.
#   -Suite logic    NativeLogicTests: Checks (and later Chance, random items, story numbers) take the branches they should.
#   -Suite places   NativePlacesTests: positions from the spouse's room stand where the game's spot is (D-52), married or not.
#                   Then the Bridge exports the save (maps on, for this suite only) and the upgraded houses' spots match the game's.
# Never starts while another Stardew Valley or SMAPI process runs. The owner's SMAPI-latest.txt is restored afterwards.
param([int]$ReadyTimeoutSeconds = 240, [ValidateSet('costume', 'camera', 'logic', 'places')][string]$Suite = 'costume')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$game = 'C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley'
$run = Join-Path $repo (Join-Path 'benches\.run' $Suite)
$log = Join-Path $env:APPDATA 'StardewValley\ErrorLogs\SMAPI-latest.txt'
$pipe = 'SDCS.Native.' + $Suite + '.' + [Guid]::NewGuid().ToString('N')
$filter = @{ costume = 'NativeCostumeTests'; camera = 'NativeCameraTests'; logic = 'NativeLogicTests'; places = 'NativePlacesTests' }[$Suite]

function Assert-NoGame {
    $running = Get-Process | Where-Object { $_.ProcessName -match 'Stardew|StardewModdingAPI' }
    if ($running) { throw "A game process is already running ($($running.Id -join ', ')). Not starting another." }
}

Assert-NoGame
foreach ($project in 'src\SDCharacterStudio.Runtime\SDCharacterStudio.Runtime.csproj', 'src\SDCharacterStudio.Bridge\SDCharacterStudio.Bridge.csproj', 'benches\BenchDriver\SdcsBench.Driver.csproj') {
    & dotnet build (Join-Path $repo $project) -c Release -v q -nologo -p:EnableModDeploy=false | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}
& dotnet build (Join-Path $repo 'src\SDCharacterStudio.Editor.Tests\SDCharacterStudio.Editor.Tests.csproj') -v q -nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Build failed: editor tests' }

if (Test-Path $run) { Remove-Item -LiteralPath $run -Recurse -Force }
$mods = Join-Path $run 'Mods'
$runtime = Join-Path $mods 'SDCharacterStudio'; $bridge = Join-Path $mods 'SDCharacterStudio.Bridge'; $driver = Join-Path $mods 'SdcsBench.Driver'
New-Item -ItemType Directory -Path $runtime, $bridge, $driver -Force | Out-Null
$src = Join-Path $repo 'src'
Copy-Item "$src\SDCharacterStudio.Runtime\bin\Release\net6.0\SDCharacterStudio.Runtime.dll", "$src\SDCharacterStudio.Runtime\bin\Release\net6.0\SDCharacterStudio.Core.dll", "$src\SDCharacterStudio.Runtime\manifest.json" $runtime
Copy-Item "$src\SDCharacterStudio.Runtime\i18n" $runtime -Recurse
Copy-Item "$src\SDCharacterStudio.Bridge\bin\Release\net6.0\SDCharacterStudio.Bridge.dll", "$src\SDCharacterStudio.Runtime\bin\Release\net6.0\SDCharacterStudio.Core.dll", "$src\SDCharacterStudio.Bridge\manifest.json" $bridge
Copy-Item "$src\SDCharacterStudio.Bridge\assets" $bridge -Recurse
Copy-Item (Join-Path $repo 'benches\BenchDriver\bin\Release\net6.0\*') $driver -Recurse
$export = Join-Path $run 'Export'
@{ ExportFolder = $export; Profile = 'costume'; AutoExportOnSaveLoaded = $false; ExportLocations = ($Suite -eq 'places'); PreviewPipeName = $pipe } |
    ConvertTo-Json | Set-Content (Join-Path $bridge 'config.json') -Encoding UTF8

$ownerLog = Join-Path $run 'owner-SMAPI-latest.txt'
if (Test-Path $log) { Copy-Item $log $ownerLog -Force }
Assert-NoGame
if (Test-Path $log) { Remove-Item -LiteralPath $log -Force }
$process = Start-Process -FilePath (Join-Path $game 'StardewModdingAPI.exe') -ArgumentList @('--mods-path', ('"' + $mods + '"'), '--no-terminal') -WorkingDirectory $game -WindowStyle Hidden -PassThru
Write-Output "Launched own isolated game, PID $($process.Id)"
$testExit = 1
try {
    $deadline = (Get-Date).AddSeconds($ReadyTimeoutSeconds)
    while (-not (Test-Path (Join-Path $driver 'ready.json'))) {
        if ($process.HasExited) { throw 'The game exited before the driver was ready.' }
        if (Test-Path (Join-Path $driver 'failed.json')) { throw ('Driver failed: ' + (Get-Content (Join-Path $driver 'failed.json') -Raw)) }
        if ((Get-Date) -gt $deadline) { throw 'Driver never became ready.' }
        Start-Sleep -Milliseconds 500
    }
    Write-Output ('Driver ready: ' + (Get-Content (Join-Path $driver 'ready.json') -Raw))
    $env:SDCS_NATIVE_PIPE = $pipe
    $env:SDCS_NATIVE_DRIVER = $driver
    $env:SDCS_NATIVE_EXPORT = Join-Path $export 'costume'
    $env:SDCS_NATIVE_EVIDENCE = $run
    $env:SDCS_NATIVE_SUITE = $Suite
    $env:SDCS_EDITOR_HOME = Join-Path $run 'editor-home'
    $ErrorActionPreference = 'Continue'   # a failing test writes to stderr; keep its message instead of aborting
    & dotnet test (Join-Path $repo 'src\SDCharacterStudio.Editor.Tests\SDCharacterStudio.Editor.Tests.csproj') --nologo --no-build --filter "FullyQualifiedName~$filter" --logger 'console;verbosity=normal' 2>&1 | Tee-Object -FilePath (Join-Path $run 'test-output.txt')
    $testExit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit(15000) | Out-Null }
    Write-Output "Stopped own game PID $($process.Id)"
    if (Test-Path $log) { Copy-Item $log (Join-Path $run 'SMAPI.txt') -Force }
    # Put the owner's log back only when no game is running (never over a live log).
    if ((Test-Path $ownerLog) -and -not (Get-Process | Where-Object { $_.ProcessName -match 'Stardew|StardewModdingAPI' })) { Copy-Item $ownerLog $log -Force }
}
if ($testExit -ne 0) { throw "The $Suite check failed ($testExit); see $run." }
Write-Output "The $Suite check passed."
