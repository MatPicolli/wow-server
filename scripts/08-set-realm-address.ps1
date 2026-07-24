<#
.SYNOPSIS
    Ajusta a tabela realmlist do banco acore_auth.

.DESCRIPTION
    Depois que voce loga, o authserver devolve pro client o endereco do
    worldserver que esta gravado na tabela realmlist. Se ele continuar com o
    valor de fabrica (127.0.0.1), so funciona na propria maquina - de outro PC
    voce autentica e trava em "Conectando...".

    Rode DEPOIS do primeiro start do worldserver, porque e ele que cria a
    tabela.

.PARAMETER Address
    Sobrescreve o RealmAddress do settings.psd1.

.PARAMETER Name
    Sobrescreve o RealmName do settings.psd1.
#>
[CmdletBinding()]
param(
    [string]$Address,
    [string]$Name,
    [int]$Port = 8085
)

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings
$m        = $settings.MySql

if (-not $Address) { $Address = $settings.RealmAddress }
if (-not $Name)    { $Name    = $settings.RealmName }

Write-Step "Conferindo se a tabela realmlist ja existe"
$check = Invoke-MySql -Settings $settings -User $m.User -Password $m.Password -Database $m.AuthDb `
                      -Sql "SHOW TABLES LIKE 'realmlist';"
if ($check -notmatch 'realmlist') {
    Write-Fail "A tabela realmlist ainda nao existe em $($m.AuthDb)." `
               "Suba o worldserver uma vez (.\scripts\start-server.ps1) e espere ele terminar de popular os bancos."
}
Write-Ok 'tabela encontrada'

Write-Step "Configurando o realm"
Write-Info "nome:     $Name"
Write-Info "endereco: $Address"
Write-Info "porta:    $Port"

# escapa aspas simples pra nao quebrar o SQL
$safeName = $Name -replace "'", "''"
$safeAddr = $Address -replace "'", "''"

$sql = @"
UPDATE realmlist
   SET name = '$safeName',
       address = '$safeAddr',
       port = $Port
 WHERE id = 1;
"@

Invoke-MySql -Settings $settings -User $m.User -Password $m.Password -Database $m.AuthDb -Sql $sql | Out-Null

$result = Invoke-MySql -Settings $settings -User $m.User -Password $m.Password -Database $m.AuthDb `
                       -Sql 'SELECT id, name, address, port FROM realmlist;'
Write-Host $result -ForegroundColor Gray

Write-Ok 'realm configurado'

if ($Address -eq '127.0.0.1') {
    Write-Info "Com 127.0.0.1 so da pra jogar nesta maquina. Pra jogar de outro PC da"
    Write-Info "sua rede, coloque o IP local aqui e no RealmAddress do settings.psd1:"
    Write-Info "  .\scripts\08-set-realm-address.ps1 -Address 192.168.0.10"
}
