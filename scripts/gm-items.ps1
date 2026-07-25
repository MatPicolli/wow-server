<#
.SYNOPSIS
    Busca itens no banco e imprime uma linha por item.

.DESCRIPTION
    A tela de itens da GUI chama este script. A consulta chega pronta da
    interface (montada e testada em ItemBrowser.BuildQuery), e aqui ela so e
    executada - assim as regras de filtro e de escape ficam num lugar so, com
    teste, em vez de duplicadas entre C# e PowerShell.

    A saida vem separada por tabulacao, uma linha por item, com um cabecalho
    que a interface descarta.

.PARAMETER Query
    O SELECT a executar. Precisa comecar com SELECT - este script nao serve
    para alterar nada.

.EXAMPLE
    .\scripts\gm-items.ps1 -Query "SELECT entry, name FROM acore_world.item_template LIMIT 5;"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Query
)

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings
$m = $settings.MySql

# Somente leitura, e de forma verificavel: qualquer coisa que nao comece com
# SELECT e recusada, e ';' no meio impediria emendar um segundo comando.
$limpo = $Query.Trim()
if ($limpo -notmatch '^(?i)select\s') {
    Write-Fail 'este script so executa SELECT.' 'A consulta recebida nao comeca com SELECT.'
}
if ($limpo.TrimEnd(';') -match ';') {
    Write-Fail 'a consulta tem mais de um comando.' 'So um SELECT por vez.'
}

if (-not (Test-MySqlReachable -Settings $settings)) {
    Write-Fail "o MySQL nao esta respondendo em $($m.Host):$($m.Port)." `
               'Rode .\scripts\start-mysql.ps1 (como Administrador).'
}

Invoke-MySql -Settings $settings -User $m.User -Password $m.Password -Sql $limpo
exit 0
