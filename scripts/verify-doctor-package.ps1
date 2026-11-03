[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $PackagePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedPackage = (Resolve-Path -LiteralPath $PackagePath -ErrorAction Stop).Path
Add-Type -AssemblyName System.IO.Compression.FileSystem

$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPackage)
try {
    $entries = @($archive.Entries |
        Where-Object { -not [string]::IsNullOrEmpty($_.Name) } |
        ForEach-Object { $_.FullName.Replace('\', '/') })
}
finally {
    $archive.Dispose()
}

$canonicalToolPrefix = 'tools/net10.0/any/'
$ambiguousPathEntries = @($entries | Where-Object {
    $segments = $_ -split '/'
    return $segments -contains '.' -or $segments -contains '..'
})
if ($ambiguousPathEntries.Count -gt 0) {
    throw "NuGet package contains $($ambiguousPathEntries.Count) file entries with ambiguous dot path segments; tool files must use canonical prefix '$canonicalToolPrefix'."
}

$toolEntries = @($entries | Where-Object { $_ -imatch '^tools/' })
$nonCanonicalToolEntries = @($toolEntries | Where-Object {
    -not $_.StartsWith($canonicalToolPrefix, [System.StringComparison]::Ordinal)
})

# Both net48 workers share the same dependency closure; each has its own executable, config and symbols.
$sharedWorkerFiles = @(
    'TiaMcpServer.Contracts.dll',
    'Microsoft.Bcl.AsyncInterfaces.dll',
    'System.Buffers.dll',
    'System.Memory.dll',
    'System.Numerics.Vectors.dll',
    'System.IO.Pipelines.dll',
    'System.Runtime.CompilerServices.Unsafe.dll',
    'System.Text.Encodings.Web.dll',
    'System.Text.Json.dll',
    'System.Threading.Tasks.Extensions.dll',
    'System.ValueTuple.dll',
    'TiaMcpServer.Contracts.pdb'
)

function Assert-WorkerSubtree {
    param(
        [Parameter(Mandatory = $true)] [string] $Folder,
        [Parameter(Mandatory = $true)] [string] $Executable
    )

    $canonicalPrefix = $canonicalToolPrefix + $Folder + '/'
    $workerEntries = @($entries | Where-Object { $_ -imatch ('(^|/)' + [regex]::Escape($Folder) + '/') })
    $canonicalEntries = @($workerEntries | Where-Object {
        $_.StartsWith($canonicalPrefix, [System.StringComparison]::Ordinal)
    })
    $nonCanonicalEntries = @($workerEntries | Where-Object {
        -not $_.StartsWith($canonicalPrefix, [System.StringComparison]::Ordinal)
    })

    if ($canonicalEntries.Count -eq 0) {
        throw "NuGet package has no canonical '$canonicalPrefix' worker subtree."
    }

    if ($nonCanonicalEntries.Count -gt 0) {
        throw "NuGet package contains $($nonCanonicalEntries.Count) non-canonical worker file entries; expected canonical prefix '$canonicalPrefix'."
    }

    $duplicateEntries = @($canonicalEntries |
        Group-Object |
        Where-Object { $_.Count -ne 1 } |
        Select-Object -ExpandProperty Name)
    if ($duplicateEntries.Count -gt 0) {
        throw "NuGet package contains $($duplicateEntries.Count) duplicate canonical worker file paths under '$canonicalPrefix'."
    }

    $requiredFiles = @("$Executable.exe", "$Executable.exe.config", "$Executable.pdb") + $sharedWorkerFiles
    $requiredFileNames = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($requiredFile in $requiredFiles) {
        [void] $requiredFileNames.Add($requiredFile)
    }

    foreach ($requiredFile in $requiredFiles) {
        $expectedEntry = $canonicalPrefix + $requiredFile
        $matches = @($canonicalEntries | Where-Object {
            [string]::Equals($_, $expectedEntry, [System.StringComparison]::OrdinalIgnoreCase)
        })
        if ($matches.Count -ne 1) {
            throw "NuGet package must contain exactly one '$expectedEntry'; found $($matches.Count)."
        }
    }

    $workerRuntimeConfigs = @($workerEntries | Where-Object {
        $_.EndsWith('runtimeconfig.json', [System.StringComparison]::OrdinalIgnoreCase)
    })
    if ($workerRuntimeConfigs.Count -gt 0) {
        throw "NuGet package must not include worker runtimeconfig files; found $($workerRuntimeConfigs.Count) under '$canonicalPrefix'."
    }

    $unexpectedWorkerFiles = @($canonicalEntries | Where-Object {
        $relativePath = $_.Substring($canonicalPrefix.Length)
        return -not $requiredFileNames.Contains($relativePath)
    })
    if ($unexpectedWorkerFiles.Count -gt 0) {
        throw "NuGet package contains $($unexpectedWorkerFiles.Count) unexpected canonical worker files under '$canonicalPrefix'."
    }

    return $requiredFiles.Count
}

$opennessFiles = Assert-WorkerSubtree -Folder 'openness-worker' -Executable 'TiaMcpServer.OpennessWorker'

if ($nonCanonicalToolEntries.Count -gt 0) {
    throw "NuGet package contains $($nonCanonicalToolEntries.Count) non-canonical tool file entries; expected canonical prefix '$canonicalToolPrefix'."
}

$plcSimFiles = Assert-WorkerSubtree -Folder 'plcsim-worker' -Executable 'TiaMcpServer.PlcSimWorker'

# The host's only Siemens dependency is the MIT-licensed SIMATIC S7-1500 Web API client from nuget.org
# (Siemens.Simatic.S7.Webserver.API, used by plc_online_read/plc_online_write). It is allowed exactly at the
# host tool root; every other Siemens assembly (Openness, PLCSIM Advanced, or the client anywhere else) fails.
$allowedHostSiemensAssembly = $canonicalToolPrefix + 'Siemens.Simatic.S7.Webserver.API.dll'
$siemensAssemblies = @($entries | Where-Object {
    [System.IO.Path]::GetFileName($_) -match '^Siemens\.(Engineering|Simatic).*\.dll$' -and
    -not [string]::Equals($_, $allowedHostSiemensAssembly, [System.StringComparison]::Ordinal)
})
if ($siemensAssemblies.Count -gt 0) {
    throw "NuGet package must not include Siemens assemblies (Openness or PLCSIM Advanced); found $($siemensAssemblies.Count)."
}

Write-Host "Verified ${resolvedPackage}: canonical openness-worker ($opennessFiles required files) and plcsim-worker ($plcSimFiles required files) subtrees, no worker runtimeconfig, no Siemens Openness or PLCSIM DLLs."
