# SDCharacterStudio benches: build the two bench mods, then play their scenes in an isolated game (Q-17).
#   Run 1 "players": only the benches (and the test driver) are installed. No SDCharacterStudio at all.
#   Run 2 "with-framework": the SDCharacterStudio framework mod is installed too; all three engines coexist.
# The code bench embeds an older engine (0.0.9-bench) than the no-code bench (current), to show copies never meet.
# The quickstart walkthrough's mod (docs/quickstart.md, made through the editor's own actions) is installed too: its
# scene must play by itself when the player walks into Robin's house, as it would for a player.
# Each run uses its own Mods folder under benches/.run and a disposable in-memory farm: no save is loaded or written.
# Never starts while another Stardew Valley or SMAPI process is running.
param([switch]$PlayersOnly)
$ErrorActionPreference = 'Stop'
$benches = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $benches '..')).Path
$game = 'C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley'
$run = Join-Path $benches '.run'
$codeVersion = '0.0.9-bench'

function Step([string]$text) { Write-Output "== $text" }
function Build([string]$project, [string[]]$extra = @()) {
    & dotnet build $project -c Release -v q -nologo @extra | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}
function Assert-NoGame {
    $running = Get-Process | Where-Object { $_.ProcessName -match 'Stardew|StardewModdingAPI' }
    if ($running) { throw "A game process is already running ($($running.Id -join ', ')). Not starting another." }
}

Assert-NoGame
$engine = Join-Path $repo 'src\SDCharacterStudio.Engine\SDCharacterStudio.Engine.csproj'
$engineOut = Join-Path $repo 'src\SDCharacterStudio.Engine\bin\Release\net6.0'
$tests = Join-Path $repo 'src\SDCharacterStudio.Editor.Tests\SDCharacterStudio.Editor.Tests.csproj'

Step "Code bench: drop in Sdcs.cs from engine $codeVersion and build the mod"
Build $engine @("-p:Version=$codeVersion")
Copy-Item (Join-Path $engineOut 'Sdcs.cs') (Join-Path $benches 'SdcsBench.CodeMod\Sdcs.cs') -Force
Build (Join-Path $benches 'SdcsBench.CodeMod\SdcsBench.CodeMod.csproj')

Step 'No-code bench and the walkthrough mod: current engine, the editor, and Export > Just this mod'
Build $engine @('-p:Version=0.1.0')
Build $tests @('-p:EnableModDeploy=false')
$env:SDCS_BENCH_EXPORT = '1'
& dotnet test $tests -c Release --no-build -nologo --filter 'FullyQualifiedName~ExportNoCodeBench|FullyQualifiedName~ExportWalkthroughBench|FullyQualifiedName~ExportStudioBench' | Out-Host
$exportExit = $LASTEXITCODE
Remove-Item Env:\SDCS_BENCH_EXPORT
if ($exportExit -ne 0) { throw 'Exporting the no-code bench or the walkthrough mod failed.' }

Step 'Bench driver and the framework mod'
Build (Join-Path $benches 'BenchDriver\SdcsBench.Driver.csproj')
Build (Join-Path $repo 'src\SDCharacterStudio.Runtime\SDCharacterStudio.Runtime.csproj') @('-p:EnableModDeploy=false')

function Invoke-Run([string]$name, [bool]$framework) {
    Assert-NoGame
    $folder = Join-Path $run $name
    if (Test-Path $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
    $mods = Join-Path $folder 'Mods'
    New-Item -ItemType Directory -Path $mods -Force | Out-Null
    Copy-Item (Join-Path $benches 'BenchDriver\bin\Release\net6.0') (Join-Path $mods 'BenchDriver') -Recurse
    Copy-Item (Join-Path $benches 'SdcsBench.CodeMod\bin\Release\net6.0') (Join-Path $mods 'SdcsBench.CodeMod') -Recurse
    Copy-Item (Join-Path $benches 'SdcsBench.NoCode\export\Bench.SdcsNoCode') (Join-Path $mods 'NoCode Bench') -Recurse
    Copy-Item (Join-Path $run 'walkthrough\My First Scene') (Join-Path $mods 'My First Scene') -Recurse
    Copy-Item (Join-Path $run 'studio\Studio Bench') (Join-Path $mods 'Studio Bench') -Recurse
    if ($framework) {
        $runtime = Join-Path $repo 'src\SDCharacterStudio.Runtime'
        $target = Join-Path $mods 'SDCharacterStudio'
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        Copy-Item (Join-Path $runtime 'bin\Release\net6.0\SDCharacterStudio.Runtime.dll'), (Join-Path $runtime 'bin\Release\net6.0\SDCharacterStudio.Core.dll'), (Join-Path $runtime 'manifest.json') $target
        Copy-Item (Join-Path $runtime 'i18n') $target -Recurse
    }
    Write-Output ("Mods: " + ((Get-ChildItem $mods -Directory).Name -join ', '))
    $process = Start-Process -FilePath (Join-Path $game 'StardewModdingAPI.exe') -ArgumentList @('--mods-path', ('"' + $mods + '"'), '--no-terminal') -WorkingDirectory $game -WindowStyle Hidden -PassThru
    $process.Id | Set-Content (Join-Path $folder 'owned-pid.txt')
    Write-Output "Launched own isolated game, PID $($process.Id)"
    $driver = Join-Path $mods 'BenchDriver'
    try {
        $deadline = (Get-Date).AddSeconds(240)
        while (-not (Test-Path (Join-Path $driver 'ready.json'))) {
            if ($process.HasExited) { throw 'The game exited before the driver was ready.' }
            if (Test-Path (Join-Path $driver 'failed.json')) { throw ('Driver failed: ' + (Get-Content (Join-Path $driver 'failed.json') -Raw)) }
            if ((Get-Date) -gt $deadline) { throw 'Driver never became ready.' }
            Start-Sleep -Milliseconds 500
        }
        $env:SDCS_NATIVE_SUITE = 'benches'
        $env:SDCS_BENCH_DRIVER = $driver
        $env:SDCS_BENCH_FRAMEWORK = $(if ($framework) { '1' } else { '0' })
        $env:SDCS_BENCH_CODE_VERSION = $codeVersion
        $env:SDCS_NATIVE_EVIDENCE = $folder
        $ErrorActionPreference = 'Continue'
        & dotnet test $tests -c Release --no-build -nologo --filter 'FullyQualifiedName~NativeBenchTests' --logger 'console;verbosity=normal' 2>&1 | Tee-Object -FilePath (Join-Path $folder 'test-output.txt') | Out-Host
        $testExit = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        foreach ($variable in 'SDCS_NATIVE_SUITE', 'SDCS_BENCH_DRIVER', 'SDCS_BENCH_FRAMEWORK', 'SDCS_BENCH_CODE_VERSION', 'SDCS_NATIVE_EVIDENCE') { Remove-Item "Env:\$variable" -ErrorAction SilentlyContinue }
        if ($testExit -ne 0) { throw "Bench run '$name' failed ($testExit). See $folder\test-output.txt." }
    }
    finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit(15000) | Out-Null }
        $log = Join-Path $env:APPDATA 'StardewValley\ErrorLogs\SMAPI-latest.txt'
        if (Test-Path $log) { Copy-Item $log (Join-Path $folder 'SMAPI.txt') -Force }
        Write-Output "Stopped own game PID $($process.Id)"
    }
}

Step 'Run 1: players (no SDCharacterStudio installed)'
Invoke-Run 'players' $false
if (-not $PlayersOnly) {
    Step 'Run 2: with the SDCharacterStudio framework installed too'
    Invoke-Run 'with-framework' $true
}
Write-Output 'Benches passed.'
