# Erzeugt assets\Anleitung.pdf aus tools\anleitung.html (mit dem in Windows enthaltenen Edge).
# Aufruf: powershell -ExecutionPolicy Bypass -File tools\make-anleitung.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
if (-not (Test-Path $edge)) { throw "Microsoft Edge wurde nicht gefunden." }

$out = Join-Path $root 'assets\Anleitung.pdf'
$html = ([Uri](Join-Path $PSScriptRoot 'anleitung.html')).AbsoluteUri
# Eigenes Profil, damit ein bereits offener Edge nicht betroffen ist.
$edgeProfile = Join-Path ([IO.Path]::GetTempPath()) ("edge-pdf-" + [Guid]::NewGuid().ToString('N'))
if (Test-Path $out) { Remove-Item $out }
try {
    Start-Process $edge -Wait -WindowStyle Hidden -ArgumentList @(
        '--headless', '--disable-gpu', "--user-data-dir=`"$edgeProfile`"",
        '--no-pdf-header-footer', "--print-to-pdf=`"$out`"", "`"$html`"")
}
finally {
    Remove-Item $edgeProfile -Recurse -Force -ErrorAction SilentlyContinue
}
if (-not (Test-Path $out)) { throw "PDF wurde nicht erzeugt." }
Write-Host "Erzeugt: $out ($((Get-Item $out).Length) Bytes)"
