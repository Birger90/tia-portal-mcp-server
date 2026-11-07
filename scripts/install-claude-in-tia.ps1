<#
.SYNOPSIS
  Builds, publishes and installs the "Claude in TIA" TIA Portal V21 Add-In (and optionally the Claude bridge).

.DESCRIPTION
  Not live-tested. One-time setup on the engineering PC (Windows, TIA Portal V21, .NET SDK 10):
    1. builds TiaMcpServer.AddIn against the INSTALLED V21 assemblies (never the reference stubs),
    2. validates TiaMcpServer.AddIn\AddInPublisherConfig.xml against the installed Siemens.Engineering.AddIn.Publisher.xsd
       (when found) and prints the schema's errors,
    3. runs Siemens.Engineering.AddIn.Publisher.exe -f <config> -o <file>.addin -l <log> -v (arguments from the V21 help
       "Generazione dell'Add-In dal file DLL"),
    4. copies the .addin into the user add-in folder (default %APPDATA%\Siemens\Automation\Portal V21\UserAddIns; this
       folder name is NOT verified - TIA Portal's task card "Add-Ins" can also add a .addin from any folder),
    5. with -InstallBridge: publishes TiaMcpServer.ClaudeBridge to %LOCALAPPDATA%\TiaMcpServer\claude-bridge, and with
       -BridgeAutostart writes "TiaMcp Claude-Bruecke.cmd" into the Startup folder (a .cmd, not a .lnk; see K44).
  Afterwards: start TIA Portal, open the task card "Add-Ins", activate "Claude in TIA" (TIA Portal asks to confirm the
  permissions), then right-click an object > "Claude".

.PARAMETER TiaPortalDir
  TIA Portal V21 installation folder (default C:\Program Files\Siemens\Automation\Portal V21).
#>
[CmdletBinding()]
param(
    [string]$TiaPortalDir = 'C:\Program Files\Siemens\Automation\Portal V21',
    [string]$ReferenceDir,
    [string]$PublisherPath,
    [string]$UserAddInDir = (Join-Path $env:APPDATA 'Siemens\Automation\Portal V21\UserAddIns'),
    [string]$OutputDir = (Join-Path $env:LOCALAPPDATA 'TiaMcpServer\claude-in-tia'),
    [switch]$InstallBridge,
    [switch]$BridgeAutostart
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).ProviderPath
$publicApi = Join-Path $TiaPortalDir 'PublicAPI'
if (-not (Test-Path $publicApi)) { throw "TIA Portal V21 PublicAPI folder not found: $publicApi (pass -TiaPortalDir)." }

if (-not $ReferenceDir) {
    $candidate = Get-ChildItem -Path $publicApi -Recurse -Filter 'Siemens.Engineering.Base.dll' -ErrorAction SilentlyContinue |
        Where-Object { (Test-Path (Join-Path $_.DirectoryName 'Siemens.Engineering.AddIn.Base.dll')) -or (Test-Path (Join-Path $_.DirectoryName 'Siemens.Engineering.AddIn.dll')) } |
        Select-Object -First 1
    if (-not $candidate) { throw "No folder below $publicApi holds Siemens.Engineering.Base.dll together with Siemens.Engineering.AddIn(.Base).dll. Pass -ReferenceDir." }
    $ReferenceDir = $candidate.DirectoryName
}
Write-Output "Add-In compile references: $ReferenceDir"

if (-not $PublisherPath) {
    $publisher = Get-ChildItem -Path $publicApi -Recurse -Filter 'Siemens.Engineering.AddIn.Publisher.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $publisher) { throw "Siemens.Engineering.AddIn.Publisher.exe not found below $publicApi. Pass -PublisherPath." }
    $PublisherPath = $publisher.FullName
}
Write-Output "Publisher: $PublisherPath"

$staging = Join-Path $OutputDir 'staging'
if (Test-Path $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging -Force | Out-Null

& dotnet build (Join-Path $repositoryRoot 'TiaMcpServer.AddIn\TiaMcpServer.AddIn.csproj') -c Release -m:1 `
    /p:UseTiaPortalReferenceStubs=false "/p:TiaAddInReferenceDir=$ReferenceDir" -o $staging
if ($LASTEXITCODE -ne 0) { throw "Building the Add-In against $ReferenceDir failed (exit $LASTEXITCODE). Every compile error here is a V21 API member the stubs assumed differently; report it." }

$config = Join-Path $staging 'AddInPublisherConfig.xml'
$xsd = Get-ChildItem -Path (Split-Path $PublisherPath) -Recurse -Filter 'Siemens.Engineering.AddIn.Publisher.xsd' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $xsd) { $xsd = Get-ChildItem -Path $publicApi -Recurse -Filter 'Siemens.Engineering.AddIn.Publisher.xsd' -ErrorAction SilentlyContinue | Select-Object -First 1 }
if ($xsd) {
    $schemaErrors = [System.Collections.Generic.List[string]]::new()
    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.ValidationType = [System.Xml.ValidationType]::Schema
    [void]$settings.Schemas.Add($null, $xsd.FullName)
    $settings.add_ValidationEventHandler({ param($sender, $eventArgs) $schemaErrors.Add("line $($eventArgs.Exception.LineNumber): $($eventArgs.Message)") })
    $reader = [System.Xml.XmlReader]::Create($config, $settings)
    try { while ($reader.Read()) { } } finally { $reader.Dispose() }
    if ($schemaErrors.Count -gt 0) {
        Write-Warning "AddInPublisherConfig.xml does not match $($xsd.FullName):"
        $schemaErrors | ForEach-Object { Write-Warning "  $_" }
        throw 'Fix TiaMcpServer.AddIn\AddInPublisherConfig.xml (namespace, permission element names) to match the schema and run again.'
    }
    Write-Output "Configuration matches $($xsd.FullName)."
} else {
    Write-Warning 'Siemens.Engineering.AddIn.Publisher.xsd was not found; the Publisher validates the configuration itself.'
}

$addin = Join-Path $OutputDir 'ClaudeInTia.addin'
$log = Join-Path $OutputDir 'publisher.log'
if (Test-Path $addin) { Remove-Item -LiteralPath $addin -Force }
Push-Location $staging
try {
    & $PublisherPath --configuration $config --outfile $addin --logfile $log --verbose
    $publisherExit = $LASTEXITCODE
} finally { Pop-Location }
if ($publisherExit -ne 0 -or -not (Test-Path $addin)) { throw "The Publisher did not create $addin (exit $publisherExit). See $log." }
Write-Output "Published $addin (log: $log)."

New-Item -ItemType Directory -Path $UserAddInDir -Force | Out-Null
Copy-Item -LiteralPath $addin -Destination (Join-Path $UserAddInDir 'ClaudeInTia.addin') -Force
Write-Output "Copied to $UserAddInDir (folder name not verified; if TIA Portal does not list it, add the .addin in the task card 'Add-Ins')."

if ($InstallBridge) {
    $bridgeDir = Join-Path $env:LOCALAPPDATA 'TiaMcpServer\claude-bridge'
    & dotnet publish (Join-Path $repositoryRoot 'TiaMcpServer.ClaudeBridge\TiaMcpServer.ClaudeBridge.csproj') -c Release -m:1 -o $bridgeDir
    if ($LASTEXITCODE -ne 0) { throw "Publishing the Claude bridge failed (exit $LASTEXITCODE)." }
    $bridgeExe = Join-Path $bridgeDir 'TiaMcpClaudeBridge.exe'
    Write-Output "Claude bridge: $bridgeExe (switch it on in TiaMcp Passwortverwaltung, tab 'Zugriffsmodus')."
    if ($BridgeAutostart) {
        $startup = [Environment]::GetFolderPath('Startup')
        $cmd = Join-Path $startup 'TiaMcp Claude-Bruecke.cmd'
        Set-Content -LiteralPath $cmd -Encoding ascii -Value @('@echo off', "start `"`" /min `"$bridgeExe`"")
        Write-Output "Autostart: $cmd"
    }
}

Write-Output ''
Write-Output 'Next: start TIA Portal V21, open the task card "Add-Ins", activate "Claude in TIA" and confirm its permissions.'
Write-Output 'Then right-click a block, device or library object > Claude > an action; replies: Claude > Claude-Antworten anzeigen.'
