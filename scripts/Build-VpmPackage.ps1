param([string]$OutputDirectory = "dist")

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repositoryRoot "package.json"
$assetRoot = Join-Path $repositoryRoot "Assets/FriendsOnlyToggles"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$resolvedOutputDirectory = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$staging = Join-Path $resolvedOutputDirectory "vpm-staging"
$outputPath = Join-Path $resolvedOutputDirectory "$($manifest.name)-$($manifest.version).zip"

New-Item -ItemType Directory -Force -Path $resolvedOutputDirectory | Out-Null
if(Test-Path -LiteralPath $staging) {
    $resolvedStaging = (Resolve-Path -LiteralPath $staging).Path
    if(!$resolvedStaging.StartsWith($resolvedOutputDirectory)) { throw "Refusing to clear unexpected staging path: $resolvedStaging" }
    Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
}
New-Item -ItemType Directory -Path $staging | Out-Null

try {
    Copy-Item -LiteralPath $manifestPath -Destination $staging
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "LICENSE") -Destination (Join-Path $staging "LICENSE.md")
    Get-ChildItem -LiteralPath $assetRoot -Force | Copy-Item -Destination $staging -Recurse
    if(Test-Path -LiteralPath $outputPath) { Remove-Item -LiteralPath $outputPath -Force }
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $outputPath -CompressionLevel Optimal
    Write-Host "Built $outputPath for $($manifest.name) $($manifest.version)."
}
finally {
    if(Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}

