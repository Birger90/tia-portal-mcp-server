@echo off
rem Installs the build prerequisites for this fork with winget (Windows asks for administrator rights).
rem   - .NET 10 SDK (global.json requires 10.0.400 or newer in the 10.0 feature bands)
rem   - PowerShell 7 (scripts\verify-reference-stubs.ps1 needs it)
rem   - .NET Framework 4.8 Developer Pack only when its targeting pack is missing: opens the official
rem     Microsoft download page, because the winget Developer Pack package is version 4.8.1.
winget install --id Microsoft.DotNet.SDK.10 --exact --accept-package-agreements --accept-source-agreements
winget install --id Microsoft.PowerShell --exact --accept-package-agreements --accept-source-agreements
if exist "%ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.dll" (
  echo .NET Framework 4.8 targeting pack: present.
) else (
  echo .NET Framework 4.8 targeting pack: missing. Opening the official Microsoft download page for the Developer Pack.
  start "" "https://dotnet.microsoft.com/en-us/download/dotnet-framework/thank-you/net48-developer-pack-offline-installer"
)
echo.
echo Done. Open a NEW window before running scripts\fork-verify.ps1, so the new PATH is used.
pause
