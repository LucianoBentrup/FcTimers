# Apaga do Discord o painel de teste e os avisos de teste publicados pelo painel-teste.ps1.
# O painel de verdade e os avisos do serviço não são tocados.
$ErrorActionPreference = 'Stop'

$idFile = Join-Path $PSScriptRoot 'painel-teste.id'
if (-not (Test-Path $idFile)) {
    Write-Host 'Nenhuma mensagem de teste registrada.'
    return
}

$secure = Read-Host 'Cole o link do webhook' -AsSecureString
$webhook = ([System.Net.NetworkCredential]::new('', $secure).Password).Trim().Split('?')[0]
if ($webhook -notmatch '^https://(canary\.|ptb\.)?discord(app)?\.com/api/webhooks/\d+/[\w-]+$') {
    throw 'Isso não parece um link de webhook do Discord.'
}

$removed = 0
foreach ($id in @(Get-Content $idFile | Where-Object { $_ })) {
    try {
        Invoke-RestMethod -Method Delete -Uri "$webhook/messages/$id" | Out-Null
        $removed++
    } catch {
        # 404: a mensagem já tinha sido apagada.
        if ($_.Exception.Response.StatusCode.value__ -ne 404) { throw }
    }
    Start-Sleep -Milliseconds 400
}

Remove-Item $idFile
Write-Host "Mensagens de teste apagadas: $removed."
