<#
.SYNOPSIS
    Cria as pecas de heranca que o WotLK nao tem: elmo, amuleto, bracadeiras,
    manoplas, cinto, grevas, botas e manto.

.DESCRIPTION
    O 3.3.5a so tem heranca de OMBRO e PEITO, 10% de XP cada - 20% no total.
    Este mod cria os 8 slots que faltam nas 6 variantes de armadura/papel do
    jogo, 48 pecas, cada uma com o mesmo feitico de +10% de XP. Com o conjunto
    completo o bonus chega a 100%.

    Isso funciona porque auras de ITENS DIFERENTES com o mesmo feitico
    empilham - o core tem comentario explicito sobre isso em SpellAuras.cpp, e e
    o mesmo motivo de ombro + peito darem 20% hoje.

    Cada peca e uma COPIA de uma heranca de verdade do seu banco, feita por
    tabela temporaria. Duas razoes:

      - a copia leva as colunas que existem NESTE banco, sem o script precisar
        conhecer o schema (este banco esta num schema mais antigo que o
        codigo-fonte);
      - os valores que fazem a heranca escalar com o nivel -
        ScalingStatDistribution e ScalingStatValue - vem prontos da peca
        original, em vez de inventados.

    O que muda em cada copia: entry, nome, slot, displayid, o bit de armadura
    dentro de ScalingStatValue e o orcamento de atributos.

    Idempotente: pode rodar quantas vezes quiser.

.PARAMETER Apply
    Grava de verdade. Sem isso so mostra o que faria.

.PARAMETER Base
    Primeiro entry da faixa. Padrao 700000, que e a faixa que este servidor
    ja usa para o conjunto.

.PARAMETER Uninstall
    Apaga as 48 pecas. As que os jogadores tiverem na bolsa viram item
    invalido - leia o aviso.

.EXAMPLE
    .\scripts\install-heirlooms.ps1
    .\scripts\install-heirlooms.ps1 -Apply
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [int]$Base = 700000,
    [switch]$Uninstall
)

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings
$m        = $settings.MySql

# --- as 6 variantes -----------------------------------------------------------
# Sao as ombreiras de heranca do proprio jogo. A ordem importa: o entry de cada
# peca e Base + (indiceDoSlot * 6) + indiceDaVariante, e mudar a ordem
# renumeraria tudo que ja foi distribuido.
$variantes = @(
    @{ I = 0; Molde = 42949; Nome = 'Polished %s of Valor';   Tecido = 'placa'  }
    @{ I = 1; Molde = 42950; Nome = "Champion's %s";          Tecido = 'malha'  }
    @{ I = 2; Molde = 42951; Nome = 'Mystical %s of Elements'; Tecido = 'malha' }
    @{ I = 3; Molde = 42952; Nome = 'Stained Shadowcraft %s'; Tecido = 'couro'  }
    @{ I = 4; Molde = 42984; Nome = 'Preened Ironfeather %s'; Tecido = 'couro'  }
    @{ I = 5; Molde = 42985; Nome = 'Tattered Dreadmist %s';  Tecido = 'tecido' }
)

# --- os 8 slots ---------------------------------------------------------------
# InventoryType: 1 cabeca, 2 pescoco, 6 cintura, 7 pernas, 8 pes, 9 pulsos,
# 10 maos, 16 capa.
#
# Orcamento de atributos: o DBC do 3.3.5a so tem DOIS niveis para heranca -
# o de ombro (0x1) e o de peito (0x8). Slots de orcamento alto (elmo, grevas)
# recebem o de peito; os menores recebem o de ombro. Bracadeiras e cinto ficam
# levemente generosos por causa disso - aproximacao deliberada, e ela erra para
# o lado forte, nao para o fraco.
#
# BitArmadura vale 0 onde a peca nao tem armadura escalada (amuleto) ou onde o
# bit e proprio do slot (capa).
$slots = @(
    @{ I = 0; Nome = 'Helm';     Tipo = 1;  Orcamento = 'peito'; Armadura = $true  }
    @{ I = 1; Nome = 'Pendant';  Tipo = 2;  Orcamento = 'ombro'; Armadura = $false }
    @{ I = 2; Nome = 'Bracers';  Tipo = 9;  Orcamento = 'ombro'; Armadura = $true  }
    @{ I = 3; Nome = 'Gloves';   Tipo = 10; Orcamento = 'ombro'; Armadura = $true  }
    @{ I = 4; Nome = 'Belt';     Tipo = 6;  Orcamento = 'ombro'; Armadura = $true  }
    @{ I = 5; Nome = 'Leggings'; Tipo = 7;  Orcamento = 'peito'; Armadura = $true  }
    @{ I = 6; Nome = 'Boots';    Tipo = 8;  Orcamento = 'ombro'; Armadura = $true  }
    @{ I = 7; Nome = 'Cloak';    Tipo = 16; Orcamento = 'ombro'; Armadura = 'capa' }
)

# Bits do ScalingStatValue, lidos de DBCStructure.h.
$ORCAMENTO = @{ ombro = 0x1;    peito = 0x8 }
$ARM_OMBRO = @{ tecido = 0x20;  couro = 0x40;  malha = 0x80;  placa = 0x100 }
$ARM_PEITO = @{ tecido = 0x100000; couro = 0x200000; malha = 0x400000; placa = 0x800000 }
$ARM_CAPA  = 0x80000

# +10% de experiencia. E o mesmo feitico das herancas originais do jogo, entao
# o cliente ja o conhece - nao precisa de patch nenhum.
$SPELL_XP = 57353

$total = $variantes.Count * $slots.Count   # 48

if (-not (Test-MySqlReachable -Settings $settings)) {
    Write-Fail "o MySQL nao esta respondendo em $($m.Host):$($m.Port)." `
               'Rode .\scripts\start-mysql.ps1 (como Administrador).'
}

$primeiro = $Base
$ultimo   = $Base + $total - 1

# ------------------------------------------------------------------ desinstalar
if ($Uninstall) {
    Write-Step "Apagar as pecas $primeiro-$ultimo"

    $quantas = Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                            -Database $m.WorldDb -Quiet `
                            -Sql "SELECT COUNT(*) FROM ``item_template`` WHERE ``entry`` BETWEEN $primeiro AND $ultimo;"
    $n = ([regex]::Match(($quantas -join "`n"), '(?m)^\s*(\d+)\s*$'))
    Write-Info ("$($n.Groups[1].Value) peca(s) no banco hoje")

    Write-Warn 'peca que um jogador tenha na bolsa ou no correio vira item invalido'
    Write-Info 'o cliente mostra "item desconhecido" e o servidor descarta no login'

    if (-not $Apply) {
        Write-Host ''
        Write-Info 'nada foi alterado. Para apagar de verdade:'
        Write-Info '  .\scripts\install-heirlooms.ps1 -Uninstall -Apply'
        exit 0
    }

    Invoke-MySql -Settings $settings -User $m.User -Password $m.Password -Database $m.WorldDb `
                 -Sql "DELETE FROM ``item_template`` WHERE ``entry`` BETWEEN $primeiro AND $ultimo;" | Out-Null
    Write-Ok 'apagadas'
    Write-Info 'no console do worldserver:  reload item_template'
    exit 0
}

# -------------------------------------------------------------------- conferir
Write-Step 'Conferindo os moldes'

# Sem os moldes nao ha o que copiar. E melhor descobrir isso agora do que a
# meio caminho, com metade do conjunto criada.
$faltando = @()
foreach ($v in $variantes) {
    $r = Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                      -Database $m.WorldDb -Quiet `
                      -Sql "SELECT ``entry``, ``Quality``, ``ScalingStatDistribution`` FROM ``item_template`` WHERE ``entry`` = $($v.Molde);"

    if (($r -join "`n") -match "(?m)^\s*$($v.Molde)\s") {
        Write-Ok ("molde {0} ({1})" -f $v.Molde, ($v.Nome -replace '%s', 'Spaulders'))
    } else {
        Write-Warn ("molde {0} nao existe em item_template" -f $v.Molde)
        $faltando += $v.Molde
    }
}

if ($faltando.Count -gt 0) {
    Write-Fail ("faltam moldes no banco: " + ($faltando -join ', ')) `
               ('Sao as ombreiras de heranca do proprio WotLK. Se o acore_world nao ' +
                'as tem, o banco do mundo nao foi importado por completo.')
}

# A coluna que carrega o bit de armadura pode nao existir num schema antigo.
$colunas = Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                        -Database $m.WorldDb -Quiet `
                        -Sql "SELECT ``COLUMN_NAME`` FROM ``information_schema``.``columns`` WHERE ``TABLE_SCHEMA`` = '$($m.WorldDb)' AND ``TABLE_NAME`` = 'item_template';"
$temScaling = ($colunas -join "`n") -match 'ScalingStatValue'
$temDist    = ($colunas -join "`n") -match 'ScalingStatDistribution'

if ($temScaling -and $temDist) {
    Write-Ok 'o banco tem ScalingStatDistribution e ScalingStatValue - os atributos vao escalar'
} else {
    Write-Warn 'este banco nao tem as colunas de escala de atributos'
    Write-Info 'as pecas serao criadas, mas com atributos fixos em vez de escalados'
}

Write-Step "Pecas a criar: $total  (entries $primeiro-$ultimo)"

# --- aparencia por slot ------------------------------------------------------
# Copiar a ombreira sem mexer no displayid daria um elmo com arte de ombreira.
# Entao para cada slot+tipo de armadura o desenho vem de um item DE VERDADE do
# seu banco, daquele slot e daquela subclasse.
#
# Filtros que importam:
#   displayid < 32000  - acima disso o cliente 3.3.5a nao resolve e mostra '?'
#                        (o ItemDisplayInfo.dbc do servidor vai a 68742, entao
#                        a validacao do lado do servidor nao pega isso)
#   ORDER BY ItemLevel DESC, entry  - determinístico, e pega arte de item alto,
#                        que combina com heranca; sem ORDER BY, rodar duas vezes
#                        daria aparencias diferentes.
Write-Step 'Escolhendo a aparencia de cada peca'

$subclasse = @{}
foreach ($v in $variantes) {
    $r = Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                      -Database $m.WorldDb -Quiet `
                      -Sql "SELECT ``subclass`` FROM ``item_template`` WHERE ``entry`` = $($v.Molde);"
    $sc = ([regex]::Match(($r -join "`n"), '(?m)^\s*(\d+)\s*$'))
    $subclasse[$v.Molde] = if ($sc.Success) { [int]$sc.Groups[1].Value } else { 0 }
}

function Get-DisplayIdParaSlot {
    # -Subclasse -1 procura em qualquer subclasse. Serve para slots onde o tipo
    # de armadura nao existe: capa e sempre tecido, entao a variante de placa
    # nao acha capa de placa nenhuma.
    param([int]$Tipo, [int]$Subclasse)

    $filtroSub = if ($Subclasse -ge 0) { "AND ``subclass`` = $Subclasse" } else { '' }

    # Excluir a propria faixa e obrigatorio. Sem isso a segunda execucao encontra
    # as pecas que a PRIMEIRA criou - a arte de uma peca passa a ser a arte que
    # ela mesma ganhou antes, e a escolha deixa de depender so do banco do jogo.
    # Era um circulo que se fechava em silencio: rodar duas vezes dava resultados
    # diferentes de rodar uma.
    $sql = @"
SELECT ``displayid`` FROM ``item_template``
 WHERE ``class`` = 4 $filtroSub AND ``InventoryType`` = $Tipo
   AND ``displayid`` > 0 AND ``displayid`` < 32000 AND ``Quality`` >= 3
   AND ``entry`` NOT BETWEEN $primeiro AND $ultimo
 ORDER BY ``ItemLevel`` DESC, ``entry`` ASC LIMIT 1;
"@
    $r = Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                      -Database $m.WorldDb -Quiet -Sql $sql
    $d = ([regex]::Match(($r -join "`n"), '(?m)^\s*(\d+)\s*$'))
    if ($d.Success) { return [int]$d.Groups[1].Value }
    return 0
}

# --- montar a lista ----------------------------------------------------------
$pecas = @()
$semArte = 0
foreach ($s in $slots) {
    foreach ($v in $variantes) {
        $entry = $Base + ($s.I * $variantes.Count) + $v.I

        $bits = $ORCAMENTO[$s.Orcamento]
        if ($s.Armadura -eq $true) {
            $bits += if ($s.Orcamento -eq 'peito') { $ARM_PEITO[$v.Tecido] } else { $ARM_OMBRO[$v.Tecido] }
        } elseif ($s.Armadura -eq 'capa') {
            $bits += $ARM_CAPA
        }

        # Amuleto e capa nao sao armadura (class 4 com subclasse de tecido nao
        # existe nesses slots), entao a arte vem de qualquer item do slot.
        $arte = Get-DisplayIdParaSlot -Tipo $s.Tipo -Subclasse $subclasse[$v.Molde]
        if ($arte -eq 0) {
            # Qualquer subclasse: melhor um desenho do slot certo com o material
            # errado do que um elmo com arte de ombreira.
            $arte = Get-DisplayIdParaSlot -Tipo $s.Tipo -Subclasse -1
        }
        if ($arte -eq 0) { $semArte++ }

        $pecas += @{
            Entry = $entry
            Nome  = ($v.Nome -replace '%s', $s.Nome)
            Molde = $v.Molde
            Tipo  = $s.Tipo
            Bits  = $bits
            Arte  = $arte
        }
    }
}

foreach ($p in $pecas) {
    $arteTexto = if ($p.Arte -gt 0) { "display $($p.Arte)" } else { 'display do molde' }
    Write-Info ("{0}  {1,-34} slot {2,-2} bits 0x{3:X}  {4}" -f `
                $p.Entry, $p.Nome, $p.Tipo, $p.Bits, $arteTexto)
}

if ($semArte -gt 0) {
    Write-Warn "$semArte peca(s) sem arte propria - ficam com o desenho da ombreira"
    Write-Info 'nao impede nada: o efeito e os atributos funcionam igual'
}

if (-not $Apply) {
    Write-Host ''
    Write-Info 'nada foi gravado. Para criar de verdade:'
    Write-Info '  .\scripts\install-heirlooms.ps1 -Apply'
    exit 0
}

# --- backup ------------------------------------------------------------------
Write-Step 'Backup do banco do mundo'

$dump = Get-MySqlDumpExe
if (-not $dump) {
    Write-Fail 'mysqldump nao encontrado.' 'Sem backup nao gravo nada.'
}

$pasta = Join-Path $settings.Root 'backups'
New-DirectoryIfMissing $pasta
$saida = Join-Path $pasta ("{0}-antes-de-herancas-{1}.sql" -f $m.WorldDb, (Get-Date -Format 'yyyy-MM-dd_HHmm'))

$prevPwd = $env:MYSQL_PWD
if ($m.Password) { $env:MYSQL_PWD = $m.Password }
$prevEap = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$codigo = -1
try {
    & $dump "--host=$($m.Host)" "--port=$($m.Port)" "--user=$($m.User)" '--protocol=TCP' `
            '--single-transaction' '--no-tablespaces' "--result-file=$saida" `
            $m.WorldDb 'item_template' 2>"$saida.err"
    $codigo = $LASTEXITCODE
} finally {
    $ErrorActionPreference = $prevEap
    $env:MYSQL_PWD = $prevPwd
}
Remove-Item "$saida.err" -Force -ErrorAction SilentlyContinue

if ($codigo -ne 0) {
    Remove-Item $saida -Force -ErrorAction SilentlyContinue
    Write-Fail "o backup falhou (codigo $codigo) - nao vou gravar nada."
}
Write-Ok ("backup: {0} ({1:N1} MB)" -f (Split-Path -Leaf $saida), ((Get-Item $saida).Length / 1MB))

# --- criar -------------------------------------------------------------------
Write-Step 'Criando'

$criadas = 0
foreach ($p in $pecas) {
    $nomeSeguro = $p.Nome -replace "'", "''"

    # Copia por tabela temporaria: leva as colunas deste banco, sem lista fixa.
    # O DELETE antes do INSERT e o que torna isso re-executavel.
    $sql = @"
DROP TEMPORARY TABLE IF EXISTS tmp_heranca;
CREATE TEMPORARY TABLE tmp_heranca SELECT * FROM ``item_template`` WHERE ``entry`` = $($p.Molde);
UPDATE tmp_heranca
   SET ``entry``         = $($p.Entry),
       ``name``          = '$nomeSeguro',
       ``InventoryType`` = $($p.Tipo),
       ``Quality``       = 7,
       ``spellid_1``     = $SPELL_XP,
       ``spelltrigger_1``= 1,
       ``BuyPrice``      = 0,
       ``SellPrice``     = 0;
"@

    if ($p.Arte -gt 0) {
        $sql += "UPDATE tmp_heranca SET ``displayid`` = $($p.Arte);`n"
    }

    if ($temScaling) {
        $sql += "UPDATE tmp_heranca SET ``ScalingStatValue`` = $($p.Bits);`n"
    }

    $sql += @"
DELETE FROM ``item_template`` WHERE ``entry`` = $($p.Entry);
INSERT INTO ``item_template`` SELECT * FROM tmp_heranca;
DROP TEMPORARY TABLE tmp_heranca;
"@

    try {
        Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                     -Database $m.WorldDb -Sql $sql -Quiet | Out-Null
        $criadas++
    } catch {
        Write-Warn ("$($p.Entry) $($p.Nome): " + "$_")
    }
}

Write-Ok "$criadas de $total gravadas"

# --- conferir de verdade -----------------------------------------------------
# Contar as linhas, nao confiar na ausencia de erro: INSERT ... SELECT de uma
# temporaria vazia devolve sucesso e insere nada.
Write-Step 'Conferencia'

$conf = Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                     -Database $m.WorldDb -Quiet `
                     -Sql "SELECT COUNT(*) FROM ``item_template`` WHERE ``entry`` BETWEEN $primeiro AND $ultimo AND ``Quality`` = 7 AND ``spellid_1`` = $SPELL_XP;"
$n = ([regex]::Match(($conf -join "`n"), '(?m)^\s*(\d+)\s*$'))
$noBanco = if ($n.Success) { [int]$n.Groups[1].Value } else { 0 }

if ($noBanco -eq $total) {
    Write-Ok "$noBanco/$total pecas no banco, qualidade heranca, com o feitico de XP"
} else {
    Write-Warn "$noBanco/$total pecas conferem - esperava $total"
}

# displayid alto nao aparece no cliente: ele mostra '?'. O servidor aceita.
$altos = Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                      -Database $m.WorldDb -Quiet `
                      -Sql "SELECT COUNT(*) FROM ``item_template`` WHERE ``entry`` BETWEEN $primeiro AND $ultimo AND ``displayid`` >= 32000;"
$na = ([regex]::Match(($altos -join "`n"), '(?m)^\s*(\d+)\s*$'))
if ($na.Success -and [int]$na.Groups[1].Value -gt 0) {
    Write-Warn ("$($na.Groups[1].Value) peca(s) com displayid >= 32000 - essas aparecem como '?' no cliente")
    Write-Info 'os moldes do jogo usam 6337-31657; se isso aparecer, o molde e que esta alto'
} else {
    Write-Ok 'nenhum displayid alto - todos resolvem no cliente'
}

Write-Step 'Proximos passos'
Write-Info 'no console do worldserver:  reload item_template'
Write-Info 'para receber o conjunto:    .\scripts\gm-heirlooms.ps1 -Character <nome>'
Write-Host ''
Write-Warn 'IMPORTANTE, e nao tem contorno pelo servidor:'
Write-Info 'estas entries nao existem no Item.dbc do CLIENTE. O efeito de XP e os'
Write-Info 'atributos funcionam, mas a peca fica sem icone na bolsa, nao equipa no'
Write-Info 'botao direito e nao faz som ao equipar. O painel de personagem mostra'
Write-Info 'ela normalmente, o que faz parecer intermitente. A correcao e entregar'
Write-Info 'ao cliente um Item.dbc com estas entries, dentro de um MPQ de patch -'
Write-Info 'isto o servidor nao consegue fazer.'

exit 0
