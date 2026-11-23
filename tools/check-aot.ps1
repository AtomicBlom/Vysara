<#
.SYNOPSIS
    NativeAOT-publishes Vysara.RoseTap.Sample and checks that diagnostics stay off in it.

.DESCRIPTION
    The sample turns Vysara.Diagnostics.Enabled on in its runtime config and starts the diagnostics
    endpoint, so it is the case that matters: under NativeAOT, Diagnostics.Enabled has to fold to false
    whatever the switch says, which removes the endpoint and every assembly load it guards. If it does
    not fold (as when [FeatureSwitchDefinition] sits on Enabled itself, so ILC replaces the whole getter
    with the switch's value), ILC keeps that code and warns about the endpoint's call into the loader.
    The JIT tests (Vysara.Tests.DiagnosticsOff among them) cannot see that, nor what the published game
    reads Enabled as, which the check runs it to find out.

    The check fails when:
      - the publish fails;
      - the publish reports IL2026 or IL3050 from Vysara's own code (a line naming Vysara). Warnings from
        other assemblies are listed but do not fail it;
      - the published sample does not print its "Diagnostics are off" line within the timeout.

    Every intermediate and output folder for the runtime is removed first, so the publish compiles and
    links everything again and reports every warning rather than only those of what changed.

    The native link finds MSVC through vswhere.exe; the Visual Studio Installer folder is put on PATH
    when vswhere is not already there.

    Paths use forward slashes throughout; PowerShell accepts them on Windows.

.EXAMPLE
    ./tools/check-aot.ps1
    Publish for this machine's runtime and check the sample.

.EXAMPLE
    ./tools/check-aot.ps1 -Runtime linux-x64
    Publish for another runtime. The published sample is run only when it is this machine's.
#>
[CmdletBinding()]
param(
    # The runtime identifier to publish for. Defaults to this machine's.
    [string] $Runtime = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier,

    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',

    # How long the published sample has to print whether diagnostics are on.
    [int] $TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'

$repo = (Split-Path $PSScriptRoot -Parent).Replace('\', '/')
$project = "$repo/Vysara.RoseTap.Sample/Vysara.RoseTap.Sample.csproj"
$out = "$repo/artifacts/aot-sample/$Runtime"

if ($IsWindows -and -not (Get-Command vswhere.exe -ErrorAction SilentlyContinue))
{
    $installer = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer"
    if (Test-Path "$installer/vswhere.exe") { $env:PATH = "$installer;$env:PATH" }
}

# The runtime's folders in every project, and the last publish, so nothing is skipped as up to date.
foreach ($folder in Get-ChildItem $repo -Directory -Filter 'Vysara*')
{
    foreach ($kind in 'obj', 'bin')
    {
        $stale = "$($folder.FullName)/$kind/$Configuration/net11.0/$Runtime"
        if (Test-Path $stale) { Remove-Item $stale -Recurse -Force }
    }
}
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

Write-Host "  publishing Vysara.RoseTap.Sample with NativeAOT ($Runtime, $Configuration) -> $out"
# Warnings are reported rather than made errors, so every one can be read and sorted by where it comes from.
# The static-graph restore keeps PublishAot from reaching the XAML source generator, a netstandard2.0 analyzer.
$log = & dotnet publish $project -c $Configuration -r $Runtime -o $out `
    -p:PublishAot=true -p:TreatWarningsAsErrors=false -p:RestoreUseStaticGraphEvaluation=true 2>&1
$published = $LASTEXITCODE -eq 0

$warnings = @($log | ForEach-Object { "$_" } | Where-Object { $_ -match '\b(IL2026|IL3050)\b' } | Sort-Object -Unique)
$ours = @($warnings | Where-Object { $_ -match 'Vysara' })
$theirs = @($warnings | Where-Object { $_ -notmatch 'Vysara' })

if (-not $published)
{
    $log | ForEach-Object { Write-Host "    $_" }
    throw "the NativeAOT publish failed (dotnet exited $LASTEXITCODE)"
}

foreach ($line in $theirs) { Write-Host "  (not Vysara's) $line" }
if ($ours.Count -gt 0)
{
    foreach ($line in $ours) { Write-Host "  $line" -ForegroundColor Red }
    throw "the NativeAOT publish reports $($ours.Count) IL2026/IL3050 warning(s) from Vysara: Diagnostics.Enabled no longer removes the code it guards"
}
Write-Host "  no IL2026 or IL3050 from Vysara"

if ($Runtime -ne [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier)
{
    Write-Host "published for $Runtime, which this machine does not run; the sample was not started"
    return
}

# The sample ticks until it is killed, so its output is read only until it says whether diagnostics are on.
$exe = Get-ChildItem $out -File | Where-Object { $_.BaseName -eq 'Vysara.RoseTap.Sample' -and $_.Extension -in '', '.exe' } | Select-Object -First 1
if (-not $exe) { throw "the publish produced no Vysara.RoseTap.Sample executable in $out" }

$start = [System.Diagnostics.ProcessStartInfo]::new($exe.FullName)
$start.RedirectStandardOutput = $true
$start.UseShellExecute = $false
$sample = [System.Diagnostics.Process]::Start($start)
try
{
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $said = $null
    while (-not $said)
    {
        $left = $deadline - [DateTime]::UtcNow
        if ($left -le [TimeSpan]::Zero) { break }
        $read = $sample.StandardOutput.ReadLineAsync()
        if (-not $read.Wait($left)) { break }
        if ($null -eq $read.Result) { break }
        Write-Host "    $($read.Result)"
        if ($read.Result -match '^Diagnostics (are off|on)') { $said = $read.Result }
    }
}
finally
{
    if (-not $sample.HasExited) { $sample.Kill() }
    $sample.Dispose()
}

if (-not $said) { throw "the published sample did not say within ${TimeoutSeconds}s whether diagnostics are on" }
if ($said -notmatch '^Diagnostics are off') { throw "the published sample has diagnostics on under NativeAOT: $said" }

Write-Host "NativeAOT keeps diagnostics off in Vysara.RoseTap.Sample, with no IL2026 or IL3050 from Vysara"
