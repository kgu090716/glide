param(
    [switch]$Test,
    [switch]$SkipRestore,
    [string]$SigningThumbprint,
    [uri]$TimestampUrl = 'http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
$projectDirectory = $PSScriptRoot
if ($SigningThumbprint) {
    . (Join-Path $projectDirectory 'installer\CodeSigning.ps1')
    $signer = Get-GlideSigner $SigningThumbprint
}
$localSdk = Join-Path $projectDirectory '.tools\dotnet\dotnet.exe'
$dotnetPath = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $projectDirectory '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $projectDirectory '.tools\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $projectDirectory '.tools\http-cache'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$projectFile = Join-Path $projectDirectory 'modern\ThreeFingerWindowDrag.csproj'
[xml]$project = Get-Content -LiteralPath $projectFile -Raw
$appVersion = @($project.Project.PropertyGroup | Where-Object { $_.Version })[0].Version
$testProject = Join-Path $projectDirectory 'tests\modern\CoreTests.csproj'
& (Join-Path $projectDirectory 'make-icon.ps1')
if (-not $SkipRestore) {
    & $dotnetPath restore $projectFile -p:Platform=x64 -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'WinUI package restore failed.' }
    if ($Test) {
        & $dotnetPath restore $testProject -v:minimal
        if ($LASTEXITCODE -ne 0) { throw 'Test restore failed.' }
    }
}
if ($Test) {
    & $dotnetPath run --project $testProject -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Input and cursor regression tests failed.' }
}
$outputDirectory = Join-Path $projectDirectory 'dist'
& $dotnetPath publish $projectFile -c Release -p:Platform=x64 --no-restore -o $outputDirectory -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'WinUI 3 publish failed.' }
$publishedExe = Join-Path $outputDirectory 'ThreeFingerWindowDrag.exe'
if ($signer) { Add-GlideSignature $signer $TimestampUrl $publishedExe }
Copy-Item -LiteralPath (Join-Path $projectDirectory 'README.md') -Destination (Join-Path $outputDirectory 'README.md') -Force
$archivePath = Join-Path $projectDirectory "Glide-$appVersion-Portable.zip"
$distributionFiles = Get-ChildItem -LiteralPath $outputDirectory | Where-Object { $_.Name -notin @('settings.ini', 'crash.log') }
Compress-Archive -LiteralPath @($distributionFiles.FullName) -DestinationPath $archivePath -Force
Write-Output "Published: $publishedExe"
Write-Output "Packaged: $archivePath"
