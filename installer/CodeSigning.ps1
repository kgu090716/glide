function Get-GlideSigner([string]$Thumbprint) {
    $Thumbprint = $Thumbprint -replace '\s'
    if ($Thumbprint -notmatch '\A[a-fA-F0-9]{40}\z') { throw 'Expected a 40-character certificate thumbprint.' }

    $certificate = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
        Where-Object Thumbprint -eq $Thumbprint | Select-Object -First 1
    $now = Get-Date
    if (!$certificate -or !$certificate.HasPrivateKey -or $now -lt $certificate.NotBefore -or $now -ge $certificate.NotAfter) {
        throw 'A current code signing certificate with a private key is required in Cert:\CurrentUser\My.'
    }

    $tool = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source
    if (!$tool) {
        $sdkPackages = Join-Path $PSScriptRoot '..\.tools\packages\microsoft.windows.sdk.buildtools'
        $tool = Get-ChildItem -Path "$sdkPackages\*\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (!$tool) { throw 'SignTool was not found on PATH or in the local Windows SDK package.' }
    [pscustomobject]@{ Tool = $tool; Thumbprint = $Thumbprint }
}

function Assert-GlideSignature($Signer, [string]$Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $Signer.Thumbprint -or
        !$signature.TimeStamperCertificate) {
        throw "Expected a valid, timestamped Authenticode signature on $Path."
    }
}

function Add-GlideSignature($Signer, [uri]$TimestampUrl, [string]$Path) {
    & $Signer.Tool sign /s My /sha1 $Signer.Thumbprint /fd SHA256 /tr $TimestampUrl.AbsoluteUri /td SHA256 $Path
    if ($LASTEXITCODE -ne 0) { throw "Signing failed for $Path." }
    Assert-GlideSignature $Signer $Path
}
