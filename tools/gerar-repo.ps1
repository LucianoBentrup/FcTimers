# Compila o plugin e gera os arquivos que o Dalamud baixa: repo.json e dist/latest.zip.
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$rawBase = 'https://raw.githubusercontent.com/LucianoBentrup/FcTimers/main'

dotnet build (Join-Path $root 'FcTimers.csproj') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'O build falhou.' }

$built = Join-Path $root 'bin\Release\FcTimers'
$manifest = Get-Content (Join-Path $built 'FcTimers.json') -Raw | ConvertFrom-Json

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
Copy-Item (Join-Path $built 'latest.zip') (Join-Path $dist 'latest.zip') -Force

$download = "$rawBase/dist/latest.zip"
$manifest | Add-Member -NotePropertyName 'RepoUrl' -NotePropertyValue 'https://github.com/LucianoBentrup/FcTimers' -Force
$manifest | Add-Member -NotePropertyName 'DownloadLinkInstall' -NotePropertyValue $download -Force
$manifest | Add-Member -NotePropertyName 'DownloadLinkUpdate' -NotePropertyValue $download -Force
$manifest | Add-Member -NotePropertyName 'DownloadLinkTesting' -NotePropertyValue $download -Force
$manifest | Add-Member -NotePropertyName 'IsHide' -NotePropertyValue $false -Force
$manifest | Add-Member -NotePropertyName 'IsTestingExclusive' -NotePropertyValue $false -Force
$manifest | Add-Member -NotePropertyName 'LastUpdate' -NotePropertyValue ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) -Force

$json = ConvertTo-Json @($manifest) -Depth 5
[System.IO.File]::WriteAllText((Join-Path $root 'repo.json'), $json, [System.Text.UTF8Encoding]::new($false))
Write-Host "Gerado: versão $($manifest.AssemblyVersion)"
