# Publica no Discord um painel de exemplo, com dados fictícios.
# Rodar de novo edita a mesma mensagem em vez de criar outra.
$ErrorActionPreference = 'Stop'

$idFile = Join-Path $PSScriptRoot 'painel-teste.id'
$secure = Read-Host 'Cole o link do webhook' -AsSecureString
$webhook = ([System.Net.NetworkCredential]::new('', $secure).Password).Trim().Split('?')[0]

if ($webhook -notmatch '^https://(canary\.|ptb\.)?discord(app)?\.com/api/webhooks/\d+/[\w-]+$') {
    throw 'Isso não parece um link de webhook do Discord.'
}

$now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
function Get-Line([string]$Name, [int]$Hours) {
    if ($Hours -le 0) { return "**$Name**: ✅ pronto" }
    $time = $now + ($Hours * 3600)
    return "**$Name**: volta <t:${time}:R> (<t:${time}:t>)"
}

$subs = @(
    Get-Line 'Navi' 3
    Get-Line 'Tatl' 5
    Get-Line 'Ciela' 0
    Get-Line 'Tael' 0
) -join "`n"

$wheelTime = $now + (70 * 3600)
$today = (Get-Date).ToString('dd/MM')
$payload = @{
    allowed_mentions = @{ parse = @() }
    embeds = @(@{
        title       = 'Timers'
        color       = 0x3B82F6
        fields      = @(
            @{ name = '🚢 Submarinos'; value = $subs }
            @{ name = '⚙️ Buffs'; value = "**Grade 3 Wheel of Confrontation**: <t:${wheelTime}:R>" }
            @{ name = "💰 Loot de hoje ($today)"; value = "**1.240.000 gil**`n**Navi**: 480.000 gil`n**Tatl**: 390.000 gil`n**Ciela**: 210.000 gil`n**Tael**: 160.000 gil" }
            @{ name = '📦 Itens de hoje'; value = "12x Item Teste A: 480.000 gil`n9x Item Teste B: 310.000 gil`n15x Item Teste C: 250.000 gil`n8x Item Teste D: 200.000 gil" }
            @{ name = '✨ Buffs ativos'; value = "**Meat and Mead II**: <t:$($now + 86392):R>`n**The Heat of Battle II**: <t:$($now + 86394):R>`n`n||Atualizado <t:${now}:R>||" }
        )
    })
} | ConvertTo-Json -Depth 6
$body = [System.Text.Encoding]::UTF8.GetBytes($payload)
$type = 'application/json; charset=utf-8'

if (Test-Path $idFile) {
    $id = (Get-Content $idFile -Raw).Trim()
    try {
        Invoke-RestMethod -Method Patch -Uri "$webhook/messages/$id" -ContentType $type -Body $body | Out-Null
        Write-Host 'Painel de teste atualizado.'
        return
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -ne 404) { throw }
    }
}

$message = Invoke-RestMethod -Method Post -Uri "${webhook}?wait=true" -ContentType $type -Body $body
Set-Content -Path $idFile -Value $message.id
Write-Host 'Painel de teste publicado.'
