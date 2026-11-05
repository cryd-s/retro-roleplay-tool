# Veröffentlicht eine neue Version auf GitHub:
#   1. Versionsnummer in src\AssemblyInfo.cs setzen
#   2. build.cmd (Tests + exe)
#   3. Commit, Tag, Push
#   4. GitHub-Release anlegen und RetroRoleplayTool.exe anhängen
#
# Aufruf: powershell -ExecutionPolicy Bypass -File tools\release.ps1 -Version 1.1.0 -Notes "Was ist neu"
# Anmeldung: nutzt die in Git gespeicherte GitHub-Anmeldung (Git Credential Manager).
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = "",
    [string]$CommitTrailer = "",
    [switch]$OnlyPublish   # nur Schritt 4 (z. B. wenn das Release beim letzten Lauf abgebrochen ist)
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version bitte als 1.2.3 angeben." }
$tag = "v$Version"
if (-not $OnlyPublish) {
if (git tag --list $tag) { throw "Tag $tag gibt es schon (zum Nachholen des Releases: -OnlyPublish)." }

# 1. Version setzen
$info = Join-Path $root 'src\AssemblyInfo.cs'
$text = [IO.File]::ReadAllText($info)
$text = $text -replace 'AssemblyVersion\("[^"]*"\)', "AssemblyVersion(""$Version.0"")"
$text = $text -replace 'AssemblyFileVersion\("[^"]*"\)', "AssemblyFileVersion(""$Version.0"")"
[IO.File]::WriteAllText($info, $text, (New-Object Text.UTF8Encoding $false))

# 2. Bauen (bricht bei roten Tests ab)
cmd /c "`"$root\build.cmd`""
if ($LASTEXITCODE -ne 0) { throw "Build oder Tests fehlgeschlagen - nichts veröffentlicht." }

# 3. Commit, Tag, Push
git add -A
git diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
    $message = "Release $tag"
    if ($Notes) { $message += "`n`n$Notes" }
    if ($CommitTrailer) { $message += "`n`n$CommitTrailer" }
    git commit -m $message
    if ($LASTEXITCODE -ne 0) { throw "Commit fehlgeschlagen." }
}
git tag -a $tag -m "Retro Roleplay Tool $tag"
git push origin HEAD --follow-tags
if ($LASTEXITCODE -ne 0) { throw "Push fehlgeschlagen." }
}

# 4. GitHub-Release
$remote = git remote get-url origin
if ($remote -notmatch 'github\.com[:/]([^/]+)/([^/.]+)') { throw "origin ist kein GitHub-Repository: $remote" }
$repo = "$($Matches[1])/$($Matches[2])"
# Anmeldung aus Git lesen. Die Anfrage muss reine LF-Zeilenenden und keine BOM haben –
# PowerShell-Pipes und .NET-Prozesseingaben fügen beides hinzu, daher über eine Datei.
$request = [IO.Path]::GetTempFileName()
try {
    [IO.File]::WriteAllBytes($request, [Text.Encoding]::ASCII.GetBytes("protocol=https`nhost=github.com`n`n"))
    $env:GIT_TERMINAL_PROMPT = '0'
    $cred = cmd /c "git credential fill < `"$request`""
}
finally {
    Remove-Item $request -ErrorAction SilentlyContinue
}
$token = ((@($cred | Where-Object { $_ -like 'password=*' }) -join '') -replace '^password=', '').Trim()
if (-not $token) { throw "Keine GitHub-Anmeldung in Git gespeichert." }
$headers = @{ Authorization = "token $token"; Accept = 'application/vnd.github+json'; 'User-Agent' = 'RetroRoleplayTool-Release' }

$body = @"
$Notes

**Download:** RetroRoleplayTool.zip unten bei *Assets* herunterladen, entpacken und RetroRoleplayTool.exe starten (Windows 10/11, keine Installation nötig).
Die exe gibt es dort auch einzeln.
"@
$release = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$repo/releases" -Headers $headers `
    -ContentType 'application/json; charset=utf-8' `
    -Body ([Text.Encoding]::UTF8.GetBytes((@{ tag_name = $tag; name = "Retro Roleplay Tool $tag"; body = $body.Trim() } | ConvertTo-Json)))

# ZIP (exe + LIESMICH.txt + Anleitung.pdf): Browser warnen bei ZIP-Downloads seltener als bei einer einzelnen exe.
$exe = Join-Path $root 'bin\RetroRoleplayTool.exe'
$zip = Join-Path $root 'bin\RetroRoleplayTool.zip'
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $exe, (Join-Path $root 'assets\LIESMICH.txt'), (Join-Path $root 'assets\Anleitung.pdf') -DestinationPath $zip

foreach ($asset in @(@{ File = $zip; Name = 'RetroRoleplayTool.zip'; Type = 'application/zip' },
                     @{ File = $exe; Name = 'RetroRoleplayTool.exe'; Type = 'application/octet-stream' })) {
    Invoke-RestMethod -Method Post -Headers $headers -ContentType $asset.Type -InFile $asset.File `
        -Uri "https://uploads.github.com/repos/$repo/releases/$($release.id)/assets?name=$($asset.Name)" | Out-Null
}

Write-Host ""
Write-Host "Veröffentlicht: $($release.html_url)"
