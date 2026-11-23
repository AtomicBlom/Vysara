<#
.SYNOPSIS
    Builds Vysara's RoseMCP tap and drops it into a RoseMCP install's taps folder.

.DESCRIPTION
    RoseMCP looks for dropped-in taps in the taps folder beside its settings.json, which is
    %LOCALAPPDATA%/BinaryVibrance/RoseMCP/taps where RoseMCP installs by default, and which installing
    RoseMCP never clears. After that it looks in the folders its settings name, then in the folders
    $env:ROSEMCP_TAPS names. The first tap found for a framework is the one that serves it.

    The tap is built into a fresh stage under artifacts/, so a failed build leaves the deployed tap in
    place. Only Vysara.RoseTap.dll and its pdb are copied. The game's diagnostics endpoint hands the tap
    the game's own Vysara, so a Vysara.dll in the taps folder would never be used, and a stale one would
    only confuse whoever reads the folder.

    A game opens that endpoint only when its runtime config turns Vysara.Diagnostics.Enabled on (see
    Vysara.RoseTap/README.md), and by default loads a tap only from this taps folder and the folders
    $env:ROSEMCP_TAPS names. RoseMCP keeps its taps folder beside its settings wherever it is installed, so
    $env:ROSEMCP_DEPLOY_ROOT doesn't move it. A game that has loaded the tap holds the file open, so close it
    before deploying again.

    Paths use forward slashes throughout; PowerShell accepts them on Windows.

.EXAMPLE
    ./tools/deploy-tap.ps1
    Build the tap and deploy it to this machine's RoseMCP install.

.EXAMPLE
    ./tools/deploy-tap.ps1 -Destination C:/Tools/taps
    Deploy it to another folder, which RoseMCP and the game look in only when $env:ROSEMCP_TAPS names it.
#>
[CmdletBinding()]
param(
    # The folder the tap goes in. Defaults to RoseMCP's taps folder, %LOCALAPPDATA%/BinaryVibrance/RoseMCP/taps.
    [string] $Destination,

    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent

if (-not $Destination)
{
    $localAppData = if ($env:LOCALAPPDATA) { $env:LOCALAPPDATA } else { Join-Path $HOME '.local/share' }
    $Destination = Join-Path $localAppData 'BinaryVibrance/RoseMCP/taps'
}

$taps = $Destination.Replace('\', '/')

# A fresh stage, because `build -o` never removes what it does not write, and a leftover from an
# earlier build would otherwise be deployed as though this one had produced it.
$stage = "$repo/artifacts/tap"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }

Write-Host "  building Vysara.RoseTap ($Configuration) -> $stage"
& dotnet build "$repo/Vysara.RoseTap/Vysara.RoseTap.csproj" -c $Configuration -o $stage | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Vysara.RoseTap failed to build (dotnet exited $LASTEXITCODE)" }

New-Item -ItemType Directory -Force -Path $taps | Out-Null
foreach ($file in 'Vysara.RoseTap.dll', 'Vysara.RoseTap.pdb')
{
    try
    {
        Copy-Item "$stage/$file" "$taps/$file" -Force
    }
    catch [System.IO.IOException]
    {
        # The endpoint loads the tap from this folder, so a game it is loaded into holds it open.
        throw "could not replace $taps/${file}: a game that has loaded the tap holds it open. Close it and deploy again. ($($_.Exception.Message))"
    }
}

Write-Host "deployed Vysara.RoseTap to $taps; it serves games RoseMCP attaches to or launches from now on"
