# Publica no Discord o painel e os avisos de exemplo, com dados fictícios e o mesmo visual do serviço.
# Rodar de novo edita o mesmo painel e troca os avisos de teste anteriores.
# Os avisos mostram o @Subs, mas não notificam ninguém.
$ErrorActionPreference = 'Stop'

$idFile = Join-Path $PSScriptRoot 'painel-teste.id'
$secure = Read-Host 'Cole o link do webhook' -AsSecureString
$webhook = ([System.Net.NetworkCredential]::new('', $secure).Password).Trim().Split('?')[0]

if ($webhook -notmatch '^https://(canary\.|ptb\.)?discord(app)?\.com/api/webhooks/\d+/[\w-]+$') {
    throw 'Isso não parece um link de webhook do Discord.'
}

$out = Join-Path ([System.IO.Path]::GetTempPath()) 'fc-timers-painel-teste'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
node (Join-Path $PSScriptRoot 'painel-teste.mjs') $out
if ($LASTEXITCODE -ne 0) { throw 'Não consegui montar o painel de teste.' }

$type = 'application/json; charset=utf-8'
$panelBody = [System.IO.File]::ReadAllBytes((Join-Path $out 'panel.json'))
$alertBodies = Get-ChildItem $out -Filter 'alert-*.json' | Sort-Object Name | ForEach-Object { , [System.IO.File]::ReadAllBytes($_.FullName) }

# Linha 1: painel. Demais linhas: avisos do teste anterior.
$ids = if (Test-Path $idFile) { @(Get-Content $idFile | Where-Object { $_ }) } else { @() }
foreach ($old in ($ids | Select-Object -Skip 1)) {
    try { Invoke-RestMethod -Method Delete -Uri "$webhook/messages/$old" | Out-Null } catch { }
}

$panelId = $null
if ($ids.Count -gt 0) {
    try {
        Invoke-RestMethod -Method Patch -Uri "$webhook/messages/$($ids[0])" -ContentType $type -Body $panelBody | Out-Null
        $panelId = $ids[0]
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -ne 404) { throw }
    }
}
if (-not $panelId) {
    $panelId = (Invoke-RestMethod -Method Post -Uri "${webhook}?wait=true" -ContentType $type -Body $panelBody).id
}

$alertIds = foreach ($alertBody in $alertBodies) {
    (Invoke-RestMethod -Method Post -Uri "${webhook}?wait=true" -ContentType $type -Body $alertBody).id
    Start-Sleep -Milliseconds 600
}

Set-Content -Path $idFile -Value (@($panelId) + @($alertIds))
Write-Host "Painel de teste publicado, com $(@($alertIds).Count) avisos de exemplo."
