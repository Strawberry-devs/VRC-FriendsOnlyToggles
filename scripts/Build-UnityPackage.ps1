param([string]$OutputPath = "dist/FriendsOnlyToggles.unitypackage")

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$assetRoot = Join-Path $repositoryRoot "Assets/FriendsOnlyToggles"
$rootMeta = "$assetRoot.meta"
$resolvedOutput = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
$outputDirectory = Split-Path -Parent $resolvedOutput
$staging = Join-Path $outputDirectory "unitypackage-staging"
$repositoryUri = [Uri]::new(([IO.Path]::GetFullPath($repositoryRoot).TrimEnd('\') + '\'))

if(!(Test-Path -LiteralPath $assetRoot) -or !(Test-Path -LiteralPath $rootMeta)) {
    throw "Assets/FriendsOnlyToggles and its .meta file are required."
}

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
if(Test-Path -LiteralPath $staging) {
    $resolvedStaging = (Resolve-Path -LiteralPath $staging).Path
    if(!$resolvedStaging.StartsWith([IO.Path]::GetFullPath($outputDirectory))) {
        throw "Refusing to clear unexpected staging path: $resolvedStaging"
    }
    Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
}
New-Item -ItemType Directory -Path $staging | Out-Null

try {
    $metaFiles = @((Get-Item -LiteralPath $rootMeta)) +
        @(Get-ChildItem -LiteralPath $assetRoot -Recurse -Filter "*.meta")
    foreach($metaFile in $metaFiles) {
        $metaText = Get-Content -LiteralPath $metaFile.FullName -Raw
        $match = [regex]::Match($metaText, '(?m)^guid:\s*([0-9a-f]{32})\s*$')
        if(!$match.Success) { throw "No Unity GUID in $($metaFile.FullName)" }

        $assetPath = $metaFile.FullName.Substring(0, $metaFile.FullName.Length - 5)
        $relativePath = [Uri]::UnescapeDataString($repositoryUri.MakeRelativeUri([Uri]::new($assetPath)).ToString())
        $entry = Join-Path $staging $match.Groups[1].Value
        New-Item -ItemType Directory -Path $entry | Out-Null
        Copy-Item -LiteralPath $metaFile.FullName -Destination (Join-Path $entry "asset.meta")
        Set-Content -LiteralPath (Join-Path $entry "pathname") -Value $relativePath -NoNewline
        # Unity folder records contain only asset.meta and pathname. Adding a zero-byte
        # "asset" makes the importer create a file where the directory should be.
        if(Test-Path -LiteralPath $assetPath -PathType Leaf) {
            Copy-Item -LiteralPath $assetPath -Destination (Join-Path $entry "asset")
        }
    }

    tar -czf $resolvedOutput -C $staging .
    if($LASTEXITCODE -ne 0) { throw "tar failed with exit code $LASTEXITCODE" }
    Write-Host "Built $resolvedOutput with $($metaFiles.Count) Unity assets."
}
finally {
    if(Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
