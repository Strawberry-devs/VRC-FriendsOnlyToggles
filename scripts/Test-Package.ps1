$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$assetRoot = Join-Path $repositoryRoot "Assets/FriendsOnlyToggles"
$manifest = Get-Content -LiteralPath (Join-Path $repositoryRoot "package.json") -Raw | ConvertFrom-Json
$repository = Get-Content -LiteralPath (Join-Path $repositoryRoot "vpm.json") -Raw | ConvertFrom-Json

$published = $repository.packages.($manifest.name).versions.($manifest.version)
if($null -eq $published) { throw "vpm.json does not publish $($manifest.name) $($manifest.version)." }
if($published.vpmDependencies.'com.vrchat.avatars' -ne $manifest.vpmDependencies.'com.vrchat.avatars') {
    throw "VPM dependency mismatch."
}

$missingMeta = @(Get-ChildItem -LiteralPath $assetRoot -Recurse -Force |
    Where-Object { $_.Extension -ne ".meta" -and !(Test-Path -LiteralPath ($_.FullName + ".meta")) })
$orphanMeta = @(Get-ChildItem -LiteralPath $assetRoot -Recurse -Filter "*.meta" |
    Where-Object { !(Test-Path -LiteralPath $_.FullName.Substring(0, $_.FullName.Length - 5)) })
if($missingMeta.Count -ne 0 -or $orphanMeta.Count -ne 0) {
    throw "Unity metadata mismatch: $($missingMeta.Count) missing and $($orphanMeta.Count) orphaned."
}

$processor = Get-Content -LiteralPath (Join-Path $assetRoot "Editor/FriendsOnlyBuildProcessor.cs") -Raw
foreach($required in "IsOnFriendsList", "IsLocal", "callbackOrder => -9000", "EnsureViewerParameter", "RewriteDirectBlendParameters", "VRCAvatarParameterDriver") {
    if(!$processor.Contains($required)) { throw "Build processor is missing '$required'." }
}

$unityPackageBuilder = Get-Content -LiteralPath (Join-Path $repositoryRoot "scripts/Build-UnityPackage.ps1") -Raw
if($unityPackageBuilder.Contains('New-Item -ItemType File -Path (Join-Path $entry "asset")')) {
    throw "Folder records must not contain an asset payload."
}

Write-Host "Package sources are consistent: post-VRCFury callback, friendship/local gating, metadata, and VPM manifest validated."
