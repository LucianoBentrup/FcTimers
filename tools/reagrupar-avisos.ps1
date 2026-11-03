# Troca os avisos de sub repetidos no canal por avisos agrupados, sem notificar de novo.
# A chave da FC é pedida aqui e não fica salva em lugar nenhum.
$ErrorActionPreference = 'Stop'

$service = 'https://fc-timers.lucianobentrup.workers.dev'
$secure = Read-Host 'Chave da FC' -AsSecureString
$key = [System.Net.NetworkCredential]::new('', $secure).Password

$response = Invoke-RestMethod -Method Post -Uri "$service/regroup" -Headers @{ Authorization = "Bearer $key" }
Write-Host "Avisos apagados: $($response.removed). Avisos agrupados enviados: $($response.sent)."
