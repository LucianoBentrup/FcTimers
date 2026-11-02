# Pede ao serviço para apagar o painel do Discord e mandar outro, que fica no fim do canal.
# A chave da FC é pedida aqui e não fica salva em lugar nenhum.
$ErrorActionPreference = 'Stop'

$service = 'https://fc-timers.lucianobentrup.workers.dev'
$secure = Read-Host 'Chave da FC' -AsSecureString
$key = [System.Net.NetworkCredential]::new('', $secure).Password

$response = Invoke-RestMethod -Method Post -Uri "$service/repost" -Headers @{ Authorization = "Bearer $key" }
if ($response.ok) {
    Write-Host 'Painel antigo apagado. O novo aparece no fim do canal em alguns segundos.'
}
