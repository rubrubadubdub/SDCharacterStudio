param([string]$GamePath = ${env:GamePath})

$callerDecompDll = $env:DECOMP_DLL
try {
    $env:DECOMP_DLL = 'Stardew Valley.dll'
    $ErrorActionPreference = 'Stop'
    $root = $PSScriptRoot
    if ($GamePath) { $env:GamePath = $GamePath }
    $decompile = Join-Path $root 'decompile'
    $xnbdump = Join-Path $root 'xnbdump'
    $decompDll = Join-Path $decompile 'bin/Debug/net6.0/decomp.dll'
    $xnbDll = Join-Path $xnbdump 'bin/Debug/net6.0/xnbdump.dll'

    function Assert($condition, [string]$message) {
        if (!$condition) { throw $message }
    }

    function Invoke-Tool([string]$dll, [string[]]$arguments, [int]$expectedExit = 0) {
        $savedPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $result = & dotnet $dll @arguments 2>&1
        $exit = $LASTEXITCODE
        $ErrorActionPreference = $savedPreference
        Assert ($exit -eq $expectedExit) "Unexpected exit $exit from $dll ($arguments): $result"
        return ($result -join "`n")
    }

    foreach ($project in @('decompile/decomp.csproj', 'xnbdump/xnbdump.csproj')) {
        Assert (Test-Path (Join-Path $root $project)) "Missing inspection tool: $project"
        & dotnet build (Join-Path $root $project) --nologo --verbosity quiet
        Assert ($LASTEXITCODE -eq 0) "Build failed: $project"
    }
    New-Item -ItemType Directory -Force (Join-Path $decompile 'out'), (Join-Path $xnbdump 'out') | Out-Null

    $source = Invoke-Tool $decompDll @('StardewValley.Event')
    Assert ($source -match 'class Event\b') 'Event decompile did not contain its class.'
    [IO.File]::WriteAllText((Join-Path $decompile 'out/Event.cs'), $source)
    $types = Invoke-Tool $decompDll @('--list', 'FarmerRenderer')
    Assert ($types -match 'StardewValley.FarmerRenderer.FarmerSpriteLayers') 'Nested types missing from listing.'
    $member = Invoke-Tool $decompDll @('StardewValley.NPC', 'tryToReceiveActiveObject')
    Assert ($member -match 'tryToReceiveActiveObject\(') 'Member decompile failed.'
    $nested = Invoke-Tool $decompDll @('StardewValley.FarmerRenderer.FarmerSpriteLayers')
    Assert ($nested -match 'enum FarmerSpriteLayers') 'Nested type decompile failed.'
    try {
        $env:DECOMP_DLL = 'StardewValley.GameData.dll'
        $data = Invoke-Tool $decompDll @('StardewValley.GameData.Characters.CharacterAppearanceData')
        Assert ($data -match 'class CharacterAppearanceData') 'DECOMP_DLL override failed.'
    } finally { $env:DECOMP_DLL = 'Stardew Valley.dll' }
    foreach ($bad in @(@(), @('Missing.Type'), @('StardewValley.Event', 'MissingMember'))) {
        $errorText = Invoke-Tool $decompDll $bad 1
        Assert ($errorText -match '(?i)usage|not found') 'Missing useful decompiler error.'
    }
    & dotnet build (Join-Path $decompile 'decomp.csproj') --nologo --verbosity quiet
    Assert ($LASTEXITCODE -eq 0) 'Generated game source entered the compile.'

    $strings = Invoke-Tool $xnbDll @('strings', 'Strings/StringsFromMaps', 'AnimalShop.')
    Assert ($strings -match '(?m)^AnimalShop\.') 'String prefix filter returned no AnimalShop strings.'
    Assert ($strings -notmatch '(?m)^(?!AnimalShop\.)[^\t\n]+\t') 'String prefix filter included another prefix.'
    $actions = Invoke-Tool $xnbDll @('map', 'Maps/ScienceHouse')
    Assert ($actions -match '(?m)^\d+\t\d+\t.+') 'Buildings actions were not emitted.'
    $layoutPath = Join-Path $xnbdump 'out/Beach layout.tsv'
    Invoke-Tool $xnbDll @('maplayout', 'Maps/Beach', $layoutPath) | Out-Null
    $layout = Get-Content -LiteralPath $layoutPath
    Assert ($layout[0] -eq "layer`tx`ty`tsheet`tindex") 'Wrong map layout header.'
    Assert ($layout.Count -eq 36401) 'Expected all 36400 tiles (104 x 50 x 7 layers), plus the header.'
    Assert (($layout | Where-Object { $_ -match '^Back\t0\t0\t' }).Count -eq 1) 'Map origin tile missing.'
    $rawPath = Join-Path $xnbdump 'out/Robin pixels.raw'
    Invoke-Tool $xnbDll @('texture', 'Characters/Robin', $rawPath) | Out-Null
    $raw = [IO.File]::ReadAllBytes($rawPath)
    Assert ([BitConverter]::ToInt32($raw, 0) -eq 64) 'Wrong Robin width.'
    Assert ([BitConverter]::ToInt32($raw, 4) -eq 320) 'Wrong Robin height.'
    Assert ([BitConverter]::ToInt32($raw, 8) -eq 0) 'Wrong Robin pixel format.'
    Assert ($raw.Length -eq 81932) 'Truncated or oversized RGBA payload.'
    $types = Invoke-Tool $xnbDll @('backtypes', 'Maps/Beach')
    Assert ($types -match '(?m)^Dirt\t[1-9]\d*') 'Beach tile sheet Type properties were not counted.'
    foreach ($bad in @(@(), @('unknown'), @('strings'), @('strings', 'Strings/StringsFromMaps'), @('texture', 'Characters/Robin'), @('map', 'Missing/Asset'))) {
        $errorText = Invoke-Tool $xnbDll $bad 1
        Assert ($errorText -match '(?i)usage|not found|could not|error') 'Missing useful XNB error.'
    }
    Write-Host 'PASS: decompiler, five vanilla XNB modes, output sizes, generated-source exclusion, and argument errors.'
} finally {
    $env:DECOMP_DLL = $callerDecompDll
}
exit 0
