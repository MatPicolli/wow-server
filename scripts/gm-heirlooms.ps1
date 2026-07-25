<#
.SYNOPSIS
    Monta os comandos que enviam todos os itens de heranca para um personagem.

.DESCRIPTION
    Nenhum ID fica escrito aqui. A lista sai do seu proprio banco:

        SELECT entry FROM item_template WHERE Quality = 7

    7 e ITEM_QUALITY_HEIRLOOM no AzerothCore (src\server\shared\SharedDefines.h),
    entao a consulta pega exatamente os itens de heranca que a sua instalacao
    tem - incluindo os que algum modulo tenha acrescentado.

    O envio e por correio, com 'send items', que funciona no console do
    worldserver e nao exige o personagem online. O limite de 12 itens por carta
    e do proprio servidor (MAX_MAIL_ITEMS), por isso os comandos saem em lotes.

    Este script NAO fala com o servidor: ele imprime os comandos. Cole no
    console do worldserver, ou use o botao correspondente na GUI.

.PARAMETER Character
    Nome do personagem que recebe as cartas.

.PARAMETER Slot
    Filtra por tipo de equipamento, pelo InventoryType do item. Sem isso, vem
    tudo. Ex.: -Slot 1 (cabeca), -Slot 3 (ombros), -Slot 16 (capa).

.PARAMETER MaxPorCarta
    Itens por carta. O padrao 12 e o teto do servidor.

.EXAMPLE
    .\scripts\gm-heirlooms.ps1 -Character Mateus

.EXAMPLE
    # so para ver o que existe, sem montar comando
    .\scripts\gm-heirlooms.ps1 -Character x -Listar
#>
[CmdletBinding()]
param(
    [string]$Character,
    [int]$Slot = -1,
    [ValidateRange(1, 12)][int]$MaxPorCarta = 12,
    [switch]$Listar
)

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings
$m = $settings.MySql

# -Listar so mostra o que existe no banco; nao ha destinatario nenhum.
if (-not $Listar) {
    if (-not $Character) {
        Write-Fail 'informe o personagem que recebe as cartas.' `
                   'Ex.: .\scripts\gm-heirlooms.ps1 -Character Mateus'
    }

    # O nome entra no comando: espaco quebraria a separacao dos argumentos e
    # aspas quebrariam o proprio comando.
    #
    # \p{L} em vez de uma faixa com letras acentuadas escritas na mao: um
    # literal acentuado depende de o arquivo ser lido na codificacao certa, e o
    # PowerShell 5.1 nao le UTF-8 sem BOM por padrao - o padrao passaria a
    # recusar nomes validos, so na maquina do usuario.
    if ($Character -notmatch '^\p{L}{2,12}$') {
        Write-Fail "nome de personagem invalido: '$Character'." `
                   'Nomes de personagem tem de 2 a 12 letras, sem espacos, numeros nem aspas.'
    }
}

if (-not (Test-MySqlReachable -Settings $settings)) {
    Write-Fail "o MySQL nao esta respondendo em $($m.Host):$($m.Port)." `
               'Rode .\scripts\start-mysql.ps1 (como Administrador).'
}

$filtro = if ($Slot -ge 0) { "AND InventoryType = $Slot" } else { '' }

# -N tira o cabecalho, -B separa por tab: sobra so os dados, faceis de partir.
$sql = "SELECT entry, name FROM ``$($m.WorldDb)``.item_template WHERE Quality = 7 $filtro ORDER BY entry;"

Write-Step 'Procurando os itens de heranca no banco'
$saida = Invoke-MySql -Settings $settings -User $m.User -Password $m.Password -Sql $sql

$itens = @()
foreach ($linha in ($saida -split "`r?`n")) {
    $t = $linha.Trim()
    if (-not $t) { continue }
    if ($t -match '^entry\s') { continue }   # cabecalho

    $partes = $t -split "`t"
    if ($partes.Count -lt 1) { continue }
    if ($partes[0] -notmatch '^\d+$') { continue }

    $itens += [pscustomobject]@{
        Id   = [int]$partes[0]
        Nome = if ($partes.Count -gt 1) { $partes[1] } else { '' }
    }
}

if ($itens.Count -eq 0) {
    Write-Warn 'nao achei nenhum item de heranca no banco'
    Write-Info 'o banco de mundo so e populado no primeiro start do worldserver -'
    Write-Info 'se o servidor ainda nao subiu, suba uma vez e tente de novo.'
    exit 1
}

Write-Ok "$($itens.Count) itens encontrados"

if ($Listar) {
    foreach ($i in $itens) { Write-Info ("{0,-8} {1}" -f $i.Id, $i.Nome) }
    exit 0
}

# --- montar as cartas --------------------------------------------------------
Write-Step "Comandos para $Character"
Write-Info "cole no console do worldserver, um por vez ($MaxPorCarta itens por carta)"
Write-Host ''

$lote = 0
for ($i = 0; $i -lt $itens.Count; $i += $MaxPorCarta) {
    $lote++
    $fatia = $itens[$i..([Math]::Min($i + $MaxPorCarta - 1, $itens.Count - 1))]
    $ids = ($fatia | ForEach-Object { $_.Id }) -join ' '

    # As aspas fazem parte do comando: assunto e mensagem sao QuotedString.
    Write-Host "send items $Character `"Kit de heranca $lote`" `"Aproveite.`" $ids"
}

Write-Host ''
Write-Info "$lote carta(s). Cada uma chega separada no correio do jogo."
Write-Info 'o correio tem um atraso padrao; se nao aparecer na hora, saia e entre de novo.'
exit 0
