# Baixa os logs de diagnóstico que os plugins enviaram ao serviço, um arquivo por personagem.
# A chave da FC é pedida aqui e não fica salva em lugar nenhum. Os arquivos vão para tools\logs, fora do git.
$ErrorActionPreference = 'Stop'

$service = 'https://fc-timers.lucianobentrup.workers.dev'
$secure = Read-Host 'Chave da FC' -AsSecureString
$key = [System.Net.NetworkCredential]::new('', $secure).Password

$response = Invoke-RestMethod -Uri "$service/logs" -Headers @{ Authorization = "Bearer $key" }
if ($response.logs.Count -eq 0) {
    Write-Host 'Nenhum log enviado ainda.'
    return
}

$folder = Join-Path $PSScriptRoot 'logs'
New-Item -ItemType Directory -Force -Path $folder | Out-Null
foreach ($log in $response.logs) {
    $sent = [DateTimeOffset]::FromUnixTimeSeconds($log.uploadedAt).ToLocalTime()
    $safe = ($log.character -replace '[^\w\- ]', '_').Trim()
    $file = Join-Path $folder "$safe.txt"
    [System.IO.File]::WriteAllText($file, $log.text, [System.Text.UTF8Encoding]::new($false))
    Write-Host "$($log.character): enviado $($sent.ToString('dd/MM HH:mm')) -> $file"
}
