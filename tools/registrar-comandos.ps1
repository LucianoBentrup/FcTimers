# Registra os comandos do FC Companion no servidor do Discord da FC.
# O Client Secret do app é pedido aqui e não fica salvo em lugar nenhum.
# Rodar de novo atualiza os comandos. A lista vem de tools/comandos.json (gerada a partir de service/src/discord.js).
$ErrorActionPreference = 'Stop'

$appId = '1574112157577117718'
$guildId = '1128490333819392112'

$secure = Read-Host 'Cole o Client Secret do app (aba OAuth2)' -AsSecureString
$secret = ([System.Net.NetworkCredential]::new('', $secure).Password).Trim()

# Token temporário só com a permissão de atualizar comandos.
$basic = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${appId}:${secret}"))
$token = Invoke-RestMethod -Method Post -Uri 'https://discord.com/api/v10/oauth2/token' `
    -Headers @{ Authorization = "Basic $basic" } `
    -ContentType 'application/x-www-form-urlencoded' `
    -Body 'grant_type=client_credentials&scope=applications.commands.update'

$commands = [System.IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'comandos.json'))
$result = Invoke-RestMethod -Method Put -Uri "https://discord.com/api/v10/applications/$appId/guilds/$guildId/commands" `
    -Headers @{ Authorization = "Bearer $($token.access_token)" } `
    -ContentType 'application/json; charset=utf-8' -Body $commands

Write-Host "Comandos registrados: $(($result | ForEach-Object { '/' + $_.name }) -join ', ')"
