$ErrorActionPreference = 'Stop'

$workflow = Join-Path $PSScriptRoot '..\.github\workflows\publish-nuget.yml'
if (-not (Test-Path -LiteralPath $workflow -PathType Leaf)) {
    throw 'The trusted NuGet publishing workflow is missing'
}

$content = Get-Content -Raw -LiteralPath $workflow
$requirements = [ordered]@{
    'manual dispatch trigger' = '(?m)^\s{2}workflow_dispatch:\s*$'
    'read-only repository contents' = '(?m)^\s{2}contents:\s+read\s*$'
    'OIDC token permission' = '(?m)^\s{2}id-token:\s+write\s*$'
    'pinned NuGet login action' = '(?m)^\s+uses:\s+NuGet/login@[0-9a-f]{40}(?:\s+#.*)?$'
    'NuGet owner identity' = '(?m)^\s+user:\s+ACX\s*$'
    'abstractions package input' = 'src/Aura\.Runtime\.DotNet\.Abstractions/Aura\.Runtime\.DotNet\.Abstractions\.csproj'
    'temporary OIDC API key' = '\$\{\{\s*steps\.login\.outputs\.NUGET_API_KEY\s*\}\}'
    'NuGet.org v3 source' = 'https://api\.nuget\.org/v3/index\.json'
}

foreach ($requirement in $requirements.GetEnumerator()) {
    if ($content -notmatch $requirement.Value) {
        throw "Trusted publishing workflow is missing $($requirement.Key)"
    }
}

if ($content -match 'secrets\.NUGET_API_KEY') {
    throw 'Trusted publishing must not reference a long-lived NuGet API key'
}

Write-Output 'Trusted NuGet publishing workflow policy verified.'
