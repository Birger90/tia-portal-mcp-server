<#
.SYNOPSIS
    One-shot Windows verification for this fork (plc_download + S7-PLCSIM Advanced tools).

.DESCRIPTION
    Runs every check that cannot run in a Linux container and writes all evidence to
    .verify\<timestamp>\ next to the repository:
      1. environment facts (SDKs, Openness group membership, TIA V21 PublicAPI, PLCSIM Advanced API)
      2. copies of the installed Openness XML documentation (Siemens.Engineering.*.xml only, no DLLs)
      3. restore, reference-stub drift check, stub build
      4. build of the solution and the locked probe against the INSTALLED V21 assemblies
         (the mechanical signature check for every Openness member the worker uses)
      5. the test suite (non-packaging and packaging) with TRX results
      6. `doctor --json`
    The script never starts TIA Portal, never opens a project, never goes online and never
    talks to a PLC or PLCSIM instance. Every step runs even if an earlier one failed; the
    summary lists each step's exit code.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\fork-verify.ps1
#>
param(
    [string]$TiaPublicApiDir = 'C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48',
    [string]$OutputRoot,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Continue'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).ProviderPath
if (-not $OutputRoot) { $OutputRoot = Join-Path $repo '.verify' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$out = Join-Path $OutputRoot $stamp
New-Item -ItemType Directory -Force -Path $out | Out-Null
$summary = New-Object System.Collections.Generic.List[string]

function Invoke-Step([string]$Name, [scriptblock]$Body) {
    $log = Join-Path $out ($Name + '.log')
    Write-Host "== $Name"
    $global:LASTEXITCODE = 0
    try {
        & $Body *>&1 | Tee-Object -FilePath $log | Out-Null
        $code = $global:LASTEXITCODE
    }
    catch {
        $_ | Out-String | Add-Content -Path $log
        $code = -1
    }
    $summary.Add(('{0,-34} exit {1}' -f $Name, $code))
}

Push-Location $repo
try {
    Invoke-Step '01-environment' {
        "Repository: $repo"
        "Commit: $(git rev-parse HEAD 2>$null)"
        "OS: $([Environment]::OSVersion.VersionString) 64-bit=$([Environment]::Is64BitOperatingSystem)"
        'dotnet SDKs:'; dotnet --list-sdks
        $refAsm = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.dll'
        ".NET Framework 4.8 targeting pack ($refAsm): exists=$(Test-Path -LiteralPath $refAsm)"
        $pwshCmd = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
        "PowerShell 7 (pwsh): $(if ($pwshCmd) { $pwshCmd } else { 'not on PATH' }); default install exists=$(Test-Path -LiteralPath (Join-Path $env:ProgramFiles 'PowerShell\7\pwsh.exe'))"
        'Openness group membership (whoami /groups):'
        whoami /groups | Select-String -SimpleMatch 'Siemens TIA Openness'
        "TIA PublicAPI dir: $TiaPublicApiDir exists=$(Test-Path -LiteralPath $TiaPublicApiDir)"
        if (Test-Path -LiteralPath $TiaPublicApiDir) {
            Get-ChildItem -LiteralPath $TiaPublicApiDir -Filter 'Siemens.Engineering*.dll' |
                ForEach-Object { '{0}  file={1}  product={2}' -f $_.Name, $_.VersionInfo.FileVersion, $_.VersionInfo.ProductVersion }
        }
        'PLCSIM Advanced (registry HKLM\SOFTWARE\Wow6432Node\Siemens\Shared Tools\PLCSIMADV_SimRT, value Path):'
        $key = 'HKLM:\SOFTWARE\Wow6432Node\Siemens\Shared Tools\PLCSIMADV_SimRT'
        if (Test-Path $key) {
            $root = (Get-ItemProperty -Path $key -Name Path -ErrorAction SilentlyContinue).Path
            "  Path=$root"
            $api = if ($root) { Join-Path $root 'API' } else { $null }
            if ($api -and (Test-Path -LiteralPath $api)) {
                Get-ChildItem -LiteralPath $api -Directory | ForEach-Object {
                    $dll = Join-Path $_.FullName 'Siemens.Simatic.Simulation.Runtime.Api.x64.dll'
                    if (Test-Path -LiteralPath $dll) {
                        $v = (Get-Item -LiteralPath $dll).VersionInfo
                        '  API {0}: {1} file={2} product={3}' -f $_.Name, $dll, $v.FileVersion, $v.ProductVersion
                    } else { '  API {0}: x64 DLL missing' -f $_.Name }
                }
            } else { '  API folder not found' }
        } else { '  not installed (registry key absent)' }
        $global:LASTEXITCODE = 0
    }

    Invoke-Step '02-openness-xml-docs' {
        $dest = Join-Path $out 'openness-xml'
        New-Item -ItemType Directory -Force -Path $dest | Out-Null
        if (Test-Path -LiteralPath $TiaPublicApiDir) {
            Get-ChildItem -LiteralPath $TiaPublicApiDir -Filter 'Siemens.Engineering*.xml' |
                ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $dest; "copied $($_.Name)" }
        } else { 'PublicAPI dir not found; nothing copied' }
        $global:LASTEXITCODE = 0
    }

    Invoke-Step '03-restore' {
        dotnet restore TiaMcpServer.slnx -m:1
        if ($LASTEXITCODE -eq 0) { dotnet restore reference-stubs/TiaMcpServer.ReferenceStubs.sln -m:1 }
    }
    Invoke-Step '04-verify-reference-stubs' {
        # verify-reference-stubs.ps1 uses .NET Core APIs (Path.TrimEndingDirectorySeparator) that
        # Windows PowerShell 5.1 lacks, so it needs PowerShell 7 (pwsh).
        $pwsh = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
        if (-not $pwsh) {
            $candidate = Join-Path $env:ProgramFiles 'PowerShell\7\pwsh.exe'
            if (Test-Path -LiteralPath $candidate) { $pwsh = $candidate }
        }
        if ($pwsh) {
            & $pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/verify-reference-stubs.ps1 -Configuration Release -NoRestore
        }
        else {
            'PowerShell 7 (pwsh) not found; install it with: winget install --id Microsoft.PowerShell --exact'
            $global:LASTEXITCODE = 1
        }
    }
    Invoke-Step '05-build-stubs' {
        dotnet build TiaMcpServer.slnx -m:1 --no-restore -c Release /p:UseTiaPortalReferenceStubs=true
    }
    Invoke-Step '06-build-installed-v21' {
        dotnet build TiaMcpServer.slnx -m:1 --no-restore -c Release /p:UseTiaPortalReferenceStubs=false "/p:TiaPortalV21Dir=$TiaPublicApiDir"
    }
    Invoke-Step '07-build-probe-installed-v21' {
        dotnet build reference-stubs/TiaMcpServer.OpennessReferenceProbe/TiaMcpServer.OpennessReferenceProbe.csproj -m:1 --no-restore -c Release /p:UseTiaPortalReferenceStubs=false "/p:TiaPortalV21Dir=$TiaPublicApiDir"
    }

    if (-not $SkipTests) {
        Invoke-Step '08-tests' {
            dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build -c Release --filter 'Category!=Packaging' --logger "trx;LogFileName=$(Join-Path $out 'tests.trx')"
        }
        Invoke-Step '09-tests-packaging' {
            dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build -c Release --filter 'Category=Packaging' --logger "trx;LogFileName=$(Join-Path $out 'tests-packaging.trx')"
        }
    }

    Invoke-Step '10-doctor' {
        $hostDll = Join-Path $repo 'TiaMcpServer/bin/Release/net10.0/TiaMcpServer.dll'
        dotnet $hostDll doctor --json
    }
}
finally {
    Pop-Location
    $summary | Set-Content -Path (Join-Path $out 'summary.txt')
    Write-Host ''
    Write-Host "Evidence written to $out"
    $summary | ForEach-Object { Write-Host $_ }
}
