[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
    [string] $OutputRoot = '',
    [switch] $CreateShortcut
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (!$OutputRoot) { $OutputRoot = Join-Path $repo 'bin' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot).TrimEnd('\', '/')
$editorOut = Join-Path $OutputRoot 'Editor'
$packageOut = Join-Path $OutputRoot 'packages'
$marker = Join-Path $OutputRoot 'sdcs-release.json'
$stamp = [Guid]::NewGuid().ToString('N')
$staging = Join-Path $OutputRoot ".sdcs-stage-$stamp"
$backup = Join-Path $OutputRoot ".sdcs-previous-$stamp"
$published = $false

function Invoke-Dotnet([string[]] $Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $($Arguments -join ' ')" }
}
function Assert-OwnedPath([string] $Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (!$full.StartsWith($OutputRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to change a path outside the release root: $full"
    }
}
function Remove-OwnedDirectory([string] $Path) {
    Assert-OwnedPath $Path
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Recurse -Force }
}
function Assert-NoGameAssemblies([string] $Root) {
    $forbidden = @('Stardew Valley.dll', 'StardewModdingAPI.dll', 'Harmony.dll', '0Harmony.dll', 'MonoGame.Framework.dll', 'Microsoft.Xna.Framework.dll', 'xTile.dll')
    $found = Get-ChildItem -LiteralPath $Root -Recurse -File | Where-Object { $forbidden -contains $_.Name }
    if ($found) { throw "Game assemblies must not be shipped: $($found.FullName -join ', ')" }
}

if ((Test-Path -LiteralPath $editorOut) -and !(Test-Path -LiteralPath $marker)) {
    throw "Output already contains an unrecognized Editor folder. Choose an empty release root: $OutputRoot"
}
if (Test-Path -LiteralPath $marker) {
    $previous = Get-Content -LiteralPath $marker -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($previous.Format -ne 1 -or $previous.Product -ne 'SDCharacterStudio') { throw 'Unrecognized release marker.' }
}
New-Item -ItemType Directory -Force -Path $staging | Out-Null
try {
    # Keep the net6 exporter resources in their stable build paths before the RID publish.
    foreach ($name in @('Engine', 'EmbedStub', 'Runtime', 'Bridge')) {
        Invoke-Dotnet @('build', (Join-Path $repo "src/SDCharacterStudio.$name/SDCharacterStudio.$name.csproj"),
            '-c', $Configuration, '-p:EnableModDeploy=false', '--nologo')
    }
    $audioPublish = Join-Path $staging 'AudioHost'
    Invoke-Dotnet @('publish', (Join-Path $repo 'src/SDCharacterStudio.AudioHost/SDCharacterStudio.AudioHost.csproj'),
        '-c', $Configuration, '-r', 'win-x64', '--self-contained', 'true', '-p:EnableModDeploy=false', '-o', $audioPublish, '--nologo')
    $editorStage = Join-Path $staging 'Editor'
    $audioProperty = '-p:AudioHostDirectory=' + $audioPublish.Replace('\', '/') + '/'
    Invoke-Dotnet @('publish', (Join-Path $repo 'src/SDCharacterStudio.Editor/SDCharacterStudio.Editor.csproj'),
        '-c', $Configuration, '-r', 'win-x64', '--self-contained', 'true', '-p:EnableModDeploy=false',
        '-p:AudioHostPublished=true', $audioProperty, '-o', $editorStage, '--nologo')
    foreach ($required in @('SDCharacterStudio.Editor.exe', 'coreclr.dll', 'AudioHost/SDCharacterStudio.AudioHost.exe',
        'AudioHost/coreclr.dll', 'Embed/SDCharacterStudio.EmbedStub.dll', 'Embed/Sdcs.cs')) {
        if (!(Test-Path -LiteralPath (Join-Path $editorStage $required) -PathType Leaf)) { throw "Incomplete release: $required" }
    }
    Assert-NoGameAssemblies $editorStage

    # Exact mod contents, with one top-level folder per zip. Never collect arbitrary build DLLs.
    $packagesStage = Join-Path $staging 'packages'
    New-Item -ItemType Directory -Path $packagesStage | Out-Null
    foreach ($kind in @('Runtime', 'Bridge')) {
        $name = "SDCharacterStudio.$kind"
        $folder = if ($kind -eq 'Runtime') { 'SDCharacterStudio' } else { $name }
        $project = Join-Path $repo "src/$name"
        $source = Join-Path $project "bin/$Configuration/net6.0"
        $mod = Join-Path $staging $folder
        New-Item -ItemType Directory -Path $mod | Out-Null
        Copy-Item -LiteralPath (Join-Path $source "$name.dll"), (Join-Path $source 'SDCharacterStudio.Core.dll'), (Join-Path $project 'manifest.json') -Destination $mod
        $data = if ($kind -eq 'Runtime') { 'i18n' } else { 'assets' }
        Copy-Item -LiteralPath (Join-Path $project $data) -Destination $mod -Recurse
        Assert-NoGameAssemblies $mod
        Compress-Archive -LiteralPath $mod -DestinationPath (Join-Path $packagesStage "$name.zip")
    }

    # Replace only our outputs, retaining exact prior files until the marker commits the release.
    New-Item -ItemType Directory -Path $backup | Out-Null
    $oldEditor = Join-Path $backup 'Editor'
    $oldMarker = Join-Path $backup 'sdcs-release.json'
    $editorSaved = $false; $editorInstalled = $false; $markerWriteStarted = $false
    $packageWrites = [Collections.Generic.List[string]]::new()
    try {
        Assert-OwnedPath $editorOut; Assert-OwnedPath $oldEditor
        if (Test-Path -LiteralPath $editorOut) {
            Move-Item -LiteralPath $editorOut -Destination $oldEditor
            $editorSaved = $true
        }
        Move-Item -LiteralPath $editorStage -Destination $editorOut
        $editorInstalled = $true
        New-Item -ItemType Directory -Path $packageOut -Force | Out-Null
        foreach ($name in @('SDCharacterStudio.Runtime.zip', 'SDCharacterStudio.Bridge.zip')) {
            $destination = Join-Path $packageOut $name
            if (Test-Path -LiteralPath $destination) {
                Copy-Item -LiteralPath $destination -Destination (Join-Path $backup $name)
            }
            $packageWrites.Add($name)
            Copy-Item -LiteralPath (Join-Path $packagesStage $name) -Destination $destination -Force
        }
        if (Test-Path -LiteralPath $marker) { Copy-Item -LiteralPath $marker -Destination $oldMarker }
        $markerWriteStarted = $true
        @{ Format = 1; Product = 'SDCharacterStudio'; Runtime = 'win-x64'; SelfContained = $true;
            BuiltUtc = [DateTime]::UtcNow.ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath $marker -Encoding UTF8
        $published = $true
    }
    finally {
        if (!$published) {
            if ($editorInstalled) { Remove-OwnedDirectory $editorOut }
            if ($editorSaved) { Move-Item -LiteralPath $oldEditor -Destination $editorOut }
            foreach ($name in $packageWrites) {
                $previousPackage = Join-Path $backup $name
                $destination = Join-Path $packageOut $name
                if (Test-Path -LiteralPath $previousPackage) {
                    Copy-Item -LiteralPath $previousPackage -Destination $destination -Force
                }
                elseif (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Force }
            }
            if ($markerWriteStarted) {
                if (Test-Path -LiteralPath $oldMarker) { Copy-Item -LiteralPath $oldMarker -Destination $marker -Force }
                elseif (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker -Force }
            }
        }
    }
    if ($CreateShortcut) {
        $shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'SD Character Studio.lnk'
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $target = Join-Path $editorOut 'SDCharacterStudio.Editor.exe'
        if ((Test-Path -LiteralPath $shortcutPath) -and $shortcut.TargetPath -ne $target) {
            throw "Refusing to overwrite an unrelated desktop shortcut: $shortcutPath"
        }
        $shortcut.TargetPath = $target; $shortcut.WorkingDirectory = $editorOut
        $shortcut.Description = 'SDCharacterStudio Editor'; $shortcut.IconLocation = "$target,0"; $shortcut.Save()
        Write-Host "Desktop shortcut: $shortcutPath"
    }
    Write-Host "Editor: $editorOut"
    Write-Host "Mod packages: $packageOut"
}
finally {
    Remove-OwnedDirectory $staging
    if ($published) { Remove-OwnedDirectory $backup }
}
