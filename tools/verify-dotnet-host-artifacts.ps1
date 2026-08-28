param(
    [Parameter(Mandatory = $true)][string]$Platform,
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$Package,
    [string]$Output
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-Condition([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$platforms = @('windows-x64', 'windows-arm64', 'linux-x64', 'linux-arm64', 'macos-x64', 'macos-arm64')
Assert-Condition ($Platform -cin $platforms) "Unsupported .NET Host platform: $Platform"
$published = (Resolve-Path -LiteralPath $PublishDirectory).Path
$packagePath = (Resolve-Path -LiteralPath $Package).Path
$executable = if ($Platform.StartsWith('windows-')) { 'aura-dotnet-runtime-host.exe' } else { 'aura-dotnet-runtime-host' }
Assert-Condition (Test-Path -LiteralPath (Join-Path $published $executable) -PathType Leaf) `
    "Published Host is missing $executable"

$archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $entries = @($archive.Entries | Where-Object { -not $_.FullName.EndsWith('/') })
    foreach ($entry in $entries) {
        Assert-Condition (-not $entry.FullName.Contains('\')) "Unsafe NPL path: $($entry.FullName)"
    }
    $nativePrefix = "native/$Platform/"
    $nativeEntries = @($entries | Where-Object { $_.FullName.StartsWith($nativePrefix) })
    $publishedFiles = @(Get-ChildItem -LiteralPath $published -Recurse -File)
    Assert-Condition ($nativeEntries.Count -eq $publishedFiles.Count) `
        'NPL native file count does not match the self-contained publish directory'
    foreach ($file in $publishedFiles) {
        $relative = $file.FullName.Substring($published.Length).TrimStart('\', '/').Replace('\', '/')
        $entry = $archive.GetEntry($nativePrefix + $relative)
        Assert-Condition ($null -ne $entry) "NPL is missing published file: $relative"
        $stream = $entry.Open()
        try {
            $algorithm = [System.Security.Cryptography.SHA256]::Create()
            try {
                $packagedHash = ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
            }
            finally { $algorithm.Dispose() }
        } finally { $stream.Dispose() }
        $sourceHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        Assert-Condition ($packagedHash -ceq $sourceHash) "NPL bytes differ for published file: $relative"
    }
    Assert-Condition ($null -ne $archive.GetEntry('plugin.json')) 'NPL is missing plugin.json'
} finally { $archive.Dispose() }

$record = [pscustomobject][ordered]@{
    platform = $Platform
    package = Split-Path -Leaf $packagePath
    sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    size = (Get-Item -LiteralPath $packagePath).Length
    processHost = $executable
}
$json = $record | ConvertTo-Json -Depth 4
if (-not [string]::IsNullOrWhiteSpace($Output)) {
    $outputPath = [System.IO.Path]::GetFullPath($Output)
    [void](New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputPath))
    [System.IO.File]::WriteAllText($outputPath, $json + "`n", [System.Text.UTF8Encoding]::new($false))
}
$json
