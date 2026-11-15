param(
    [switch]$SkipPublish,
    [string]$SigningThumbprint,
    [uri]$TimestampUrl = 'http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
if ($SigningThumbprint) {
    . (Join-Path $PSScriptRoot 'installer\CodeSigning.ps1')
    $signer = Get-GlideSigner $SigningThumbprint
}
if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Test -SigningThumbprint $SigningThumbprint -TimestampUrl $TimestampUrl
}
$compiler = Join-Path $PSScriptRoot '.tools\inno\ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $availableCompiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if (-not $availableCompiler) { throw 'Inno Setup 7 compiler not found. Install it or place its portable compiler in .tools\inno.' }
    $compiler = $availableCompiler.Source
}
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'modern\ThreeFingerWindowDrag.csproj')
$appVersion = @($project.Project.PropertyGroup | Where-Object { $_.Version })[0].Version
$publishedExe = Join-Path $PSScriptRoot 'dist\ThreeFingerWindowDrag.exe'
if (-not (Test-Path -LiteralPath $publishedExe)) { throw 'Publish the app before building its installer.' }
if ((Get-Item -LiteralPath $publishedExe).VersionInfo.FileVersion -ne "$appVersion.0") { throw 'Published app version does not match the installer version.' }
if ($signer) { Assert-GlideSignature $signer $publishedExe }
& $compiler '/Qp' "/DMyAppVersion=$appVersion" (Join-Path $PSScriptRoot 'installer\Glide.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$installer = Join-Path $PSScriptRoot "releases\Glide-$appVersion-Setup.exe"
if ($signer) { Add-GlideSignature $signer $TimestampUrl $installer }
Write-Output $installer
