<#
.SYNOPSIS
    Instala o Sistema de Prestigio (mods\prestige) no servidor.

.DESCRIPTION
    O mod e escrito em Lua sobre o ALE, entao nao precisa recompilar o core.
    Instalar sao quatro coisas:

      1. duas tabelas de SQL no banco de personagens
      2. os .lua em <ServerDir>\lua_scripts\
      3. a pasta extensions\ do build (o StackTracePlus vem dela - sem ele
         erro de Lua aparece sem numero de linha)
      4. um NPC de gossip em creature_template
      5. o addon dentro do client (opcional: sem ele os bonus funcionam,
         so nao aparece o icone)

    Mostra o que faria e nao mexe em nada sem -Apply.

    ATENCAO: o mod em si faz uma operacao destrutiva nos personagens - quem
    prestigia perde nivel, quests e conquistas, e recebe o equipamento de
    volta pelo correio SEM encantamentos nem gemas. Isso e o mod, nao este
    script. Leia mods\prestige\README.md antes de liberar para uso.

.PARAMETER Apply
    Instala de verdade.

.PARAMETER SkipAddon
    Nao copia o addon para o client.

.PARAMETER SkipNpc
    Nao cria o NPC.

.PARAMETER BaseEntry
    Entry de creature_template usado como molde do NPC. Sem isso o script
    escolhe sozinho um humanoide que ja tenha gossip e modelo no SEU banco.

.PARAMETER Uninstall
    Remove os .lua e o NPC. Nao apaga as tabelas: elas guardam o prestigio
    dos personagens.

.EXAMPLE
    .\scripts\install-prestige.ps1
    .\scripts\install-prestige.ps1 -Apply
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$SkipAddon,
    [switch]$SkipNpc,
    [int]$BaseEntry = 0,
    [switch]$Uninstall
)

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings
$m        = $settings.MySql

$origem     = Join-Path $AcRepoDir 'mods\prestige'
$luaOrigem  = Join-Path $origem 'lua_scripts'
$sqlOrigem  = Join-Path $origem 'sql'
$addonOrig  = Join-Path $origem 'addon\PrestigeUI'

$luaDestino = Join-Path $settings.ServerDir 'lua_scripts'

if (-not (Test-Path $luaOrigem)) {
    Write-Fail "nao achei $luaOrigem." 'O repositorio esta incompleto - atualize com git pull.'
}

# ---------------------------------------------------------------- desinstalar
if ($Uninstall) {
    Write-Step 'Desinstalar o Sistema de Prestigio'

    $arquivos = Get-ChildItem $luaOrigem -Filter '*.lua' -File | ForEach-Object {
        Join-Path $luaDestino $_.Name
    } | Where-Object { Test-Path $_ }

    if ($arquivos) {
        foreach ($a in $arquivos) { Write-Info "apagaria $a" }
    } else {
        Write-Info 'nenhum .lua do prestigio esta instalado'
    }

    Write-Info 'as tabelas character_prestige* NAO sao apagadas:'
    Write-Info 'elas guardam o prestigio ja conquistado pelos personagens'

    if (-not $Apply) {
        Write-Host ''
        Write-Info 'nada foi alterado. Para desinstalar de verdade:'
        Write-Info '  .\scripts\install-prestige.ps1 -Uninstall -Apply'
        exit 0
    }

    foreach ($a in $arquivos) { Remove-Item $a -Force; Write-Ok "removido $(Split-Path -Leaf $a)" }
    Write-Info 'reinicie o worldserver para o ALE deixar de carregar os scripts'
    exit 0
}

# ------------------------------------------------------------------ conferir
Write-Step 'Conferindo o que e necessario'

# O mod depende do ALE. Sem ele os .lua ficam na pasta e nada acontece - e o
# sintoma e "instalei e nao funciona", sem erro nenhum no log.
$modAle   = Join-Path (Join-Path $settings.SourceDir 'modules') 'mod-ale'
$modEluna = Join-Path (Join-Path $settings.SourceDir 'modules') 'mod-eluna'

if (Test-Path $modAle) {
    Write-Ok 'mod-ale instalado'
} elseif (Test-Path $modEluna) {
    Write-Warn 'o modulo Lua esta como mod-eluna, nome que o core nao reconhece'
    Write-Info 'rode .\scripts\fix-module-name.ps1 -Apply e recompile antes de seguir'
} else {
    Write-Warn 'mod-ale nao esta instalado'
    Write-Info 'sem o engine Lua estes scripts nunca sao executados'
    Write-Info 'instale pela aba Modulos da GUI e recompile'
}

$arquivosLua = Get-ChildItem $luaOrigem -Filter '*.lua' -File | Sort-Object Name
Write-Info "$($arquivosLua.Count) script(s) Lua a copiar"
foreach ($a in $arquivosLua) { Write-Info "  $($a.Name)" }

# extensions\ vem da saida do build; o StackTracePlus mora la.
$extDestino = Join-Path $luaDestino 'extensions'
$extOrigem  = Join-Path (Join-Path $settings.BuildDir "bin\$($settings.BuildConfig)") 'lua_scripts\extensions'

if (Test-Path $extDestino) {
    Write-Ok 'extensions\ ja esta no lugar'
} elseif (Test-Path $extOrigem) {
    Write-Info "copiaria extensions\ de $extOrigem"
} else {
    Write-Warn 'nao achei extensions\ nem no destino nem no build'
    Write-Info 'sem StackTracePlus, erro de Lua aparece sem numero de linha'
}

if (-not $SkipAddon) {
    $addonDestino = Join-Path $settings.ClientDir 'Interface\AddOns\PrestigeUI'
    if (Test-Path (Join-Path $settings.ClientDir 'Wow.exe')) {
        Write-Info "addon iria para $addonDestino"
    } else {
        Write-Warn "ClientDir '$($settings.ClientDir)' nao tem Wow.exe - o addon sera pulado"
        $SkipAddon = $true
    }
}

if (-not (Test-MySqlReachable -Settings $settings)) {
    Write-Fail "o MySQL nao esta respondendo em $($m.Host):$($m.Port)." `
               'Rode .\scripts\start-mysql.ps1 (como Administrador).'
}

# Molde do NPC: escolhido no banco do usuario, nao chutado. Precisa de gossip
# (npcflag & 1) e de modelo, senao o log reclama de display id faltando.
$temModelTable = $false
$moldeNpc = 0

# Tres estados diferentes, e confundi-los foi bug: SkipNpc e "o usuario nao quis",
# npcOk e "conseguiu". Quem pediu o NPC e nao conseguiu tem que ver isso no
# resumo e no codigo de saida - antes, molde inexistente virava SkipNpc e a
# instalacao terminava dizendo que estava tudo certo.
$npcOk = $true

if (-not $SkipNpc) {
    $checkModel = Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                               -Database $m.WorldDb -Sql "SHOW TABLES LIKE 'creature_template_model';" -Quiet
    $temModelTable = ($checkModel -match 'creature_template_model')

    if ($BaseEntry -gt 0) {
        $moldeNpc = $BaseEntry
    } else {
        # ORDER BY entry para dar sempre o mesmo molde: instalacao que muda de
        # aparencia a cada execucao seria confusa sem motivo.
        #
        # Todo nome de coluna vai entre crases. 'rank' e palavra RESERVADA no
        # MySQL 8 (a funcao de janela RANK()), e sem crase o servidor devolve
        # ERROR 1064 apontando para 'rank' como se fosse erro de sintaxe. O
        # MariaDB aceita sem crase, entao um teste local passa e o MySQL 8 do
        # usuario falha - foi exatamente o que aconteceu aqui.
        $sqlMolde = if ($temModelTable) {
            @"
SELECT ct.``entry`` FROM ``creature_template`` ct
  JOIN ``creature_template_model`` mdl ON mdl.``CreatureID`` = ct.``entry``
 WHERE ct.``npcflag`` & 1 AND ct.``type`` = 7 AND ct.``rank`` = 0
 ORDER BY ct.``entry`` LIMIT 1;
"@
        } else {
            @"
SELECT ``entry`` FROM ``creature_template``
 WHERE ``npcflag`` & 1 AND ``type`` = 7 AND ``rank`` = 0 AND ``modelid1`` > 0
 ORDER BY ``entry`` LIMIT 1;
"@
        }

        $saida = Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                              -Database $m.WorldDb -Sql $sqlMolde -Quiet
        $achado = [regex]::Match(($saida -join "`n"), '(?m)^\s*(\d+)\s*$')
        if ($achado.Success) { $moldeNpc = [int]$achado.Groups[1].Value }
    }

    # Molde que nao existe nao da erro nenhum: a tabela temporaria sai vazia, o
    # INSERT insere zero linhas, o mysql devolve 0 e o script diria "copiado" sem
    # ter criado NPC nenhum. Entao a existencia e conferida aqui.
    if ($moldeNpc -gt 0) {
        $existeMolde = Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                                    -Database $m.WorldDb -Quiet `
                                    -Sql "SELECT COUNT(*) FROM ``creature_template`` WHERE ``entry`` = $moldeNpc;"
        $quantos = ([regex]::Match(($existeMolde -join "`n"), '(?m)^\s*(\d+)\s*$'))

        if (-not $quantos.Success -or [int]$quantos.Groups[1].Value -eq 0) {
            Write-Warn "o molde $moldeNpc nao existe em creature_template"
            Write-Info 'passe -BaseEntry com um entry que exista, ou deixe o script escolher'
            $moldeNpc = 0
            $npcOk = $false
        }
    }

    if ($moldeNpc -gt 0) {
        Write-Ok "molde do NPC: creature_template entry $moldeNpc"
    } elseif ($npcOk) {
        Write-Warn 'nao achei um NPC de gossip para usar como molde'
        Write-Info 'passe -BaseEntry <entry> ou crie o NPC a mao (ver README do mod)'
        $npcOk = $false
    }
}

# O entry do NPC tem que ser o mesmo que o config Lua espera, senao clicar no
# NPC nao abre menu - e o sintoma e igual a script quebrado.
$npcEntry = 190000
$config = Join-Path $luaOrigem '01_prestige_config.lua'
$achadoEntry = [regex]::Match((Get-Content -Raw $config), '(?m)^\s*NPC_ENTRY\s*=\s*(\d+)')
if ($achadoEntry.Success) { $npcEntry = [int]$achadoEntry.Groups[1].Value }
Write-Info "NPC_ENTRY do config: $npcEntry"

if (-not $Apply) {
    Write-Host ''
    Write-Info 'nada foi alterado. Para instalar de verdade:'
    Write-Info '  .\scripts\install-prestige.ps1 -Apply'
    exit 0
}

# --------------------------------------------------------------------- SQL --
Write-Step "Tabelas em $($m.CharDb)"

# Ordem obrigatoria: o upgrade adiciona coluna na tabela que o primeiro cria.
foreach ($nome in @('prestige_characters.sql', 'prestige_maxlevel_upgrade.sql')) {
    $arquivo = Join-Path $sqlOrigem $nome
    if (-not (Test-Path $arquivo)) { Write-Fail "nao achei $arquivo" }

    Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                 -Database $m.CharDb -File $arquivo | Out-Null
    Write-Ok $nome
}

$tabelas = Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                        -Database $m.CharDb -Sql "SHOW TABLES LIKE 'character_prestige%';"
Write-Host $tabelas -ForegroundColor DarkGray

# --------------------------------------------------------------------- Lua --
Write-Step "Scripts em $luaDestino"
New-DirectoryIfMissing $luaDestino

foreach ($a in $arquivosLua) {
    Copy-Item $a.FullName $luaDestino -Force
    Write-Ok $a.Name
}

if (-not (Test-Path $extDestino) -and (Test-Path $extOrigem)) {
    Copy-Item $extOrigem $luaDestino -Recurse -Force
    Write-Ok 'extensions\'
}

# --------------------------------------------------------------------- NPC --
# Falha aqui nao derruba o resto: quando o NPC quebrou, as tabelas e os scripts
# Lua ja estavam instalados, e a excecao escondia isso - a saida terminava num
# stack trace, sem dizer o que tinha dado certo nem o que faltava. Agora o passo
# e isolado e o resumo do fim conta a verdade.
$npcErro = ''

if (-not $SkipNpc -and $moldeNpc -gt 0) {
    Write-Step "NPC $npcEntry em $($m.WorldDb)"

    # A copia passa por tabela temporaria de proposito: assim ela leva as
    # colunas que EXISTEM neste banco, sem o script precisar conhecer o schema.
    # Este banco esta num schema mais antigo que o codigo-fonte, e listar
    # colunas a mao daria 'Unknown column' em alguma delas.
    # Crase em TODO nome de coluna. 'rank' e reservada no MySQL 8 e sem crase
    # sai ERROR 1064 apontando para ela como erro de sintaxe; o MariaDB aceita
    # sem, o que faz um teste local passar e o servidor de verdade falhar.
    $sqlNpc = @"
DROP TEMPORARY TABLE IF EXISTS tmp_prestige_npc;
CREATE TEMPORARY TABLE tmp_prestige_npc SELECT * FROM ``creature_template`` WHERE ``entry`` = $moldeNpc;
UPDATE tmp_prestige_npc
   SET ``entry``      = $npcEntry,
       ``name``       = 'Guardiao do Prestigio',
       ``subname``    = 'Ascensao',
       ``npcflag``    = 1,
       ``ScriptName`` = '',
       ``minlevel``   = 80,
       ``maxlevel``   = 80,
       ``faction``    = 35,
       ``rank``       = 0;
DELETE FROM ``creature_template`` WHERE ``entry`` = $npcEntry;
INSERT INTO ``creature_template`` SELECT * FROM tmp_prestige_npc;
DROP TEMPORARY TABLE tmp_prestige_npc;
"@

    try {
        Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                     -Database $m.WorldDb -Sql $sqlNpc | Out-Null

        # Conferir a linha, nao a ausencia de excecao: INSERT ... SELECT de uma
        # tabela vazia devolve sucesso sem inserir nada.
        $conferido = Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                                  -Database $m.WorldDb -Quiet `
                                  -Sql "SELECT COUNT(*) FROM ``creature_template`` WHERE ``entry`` = $npcEntry;"
        $achou = ([regex]::Match(($conferido -join "`n"), '(?m)^\s*(\d+)\s*$'))

        if ($achou.Success -and [int]$achou.Groups[1].Value -gt 0) {
            Write-Ok "creature_template (copiado de $moldeNpc, npcflag = 1)"
        } else {
            $npcOk = $false
            Write-Warn "o comando rodou mas nao ha linha com entry $npcEntry"
            Write-Info "o molde $moldeNpc provavelmente nao existe neste banco"
        }
    } catch {
        $npcOk = $false
        # "$_" e nao $_: um ErrorRecord impresso direto vem enfeitado pelo
        # PowerShell e a mensagem real se perde no meio da decoracao.
        $npcErro = "$_"
        Write-Warn 'nao consegui criar o NPC'
        foreach ($linha in ($npcErro -split "`r?`n")) {
            if ($linha.Trim()) { Write-Info "  $linha" }
        }
        Write-Info 'a copia passa por tabela temporaria, e o INSERT so roda depois'
        Write-Info 'do UPDATE - entao creature_template nao ficou pela metade'
    }

    if ($npcOk -and $temModelTable) {
        # Sem linha aqui o log de inicializacao avisa
        # "does not have any existing display id in creature_template_model"
        # e o NPC nasce invisivel.
        $sqlModelo = @"
DROP TEMPORARY TABLE IF EXISTS tmp_prestige_model;
CREATE TEMPORARY TABLE tmp_prestige_model SELECT * FROM ``creature_template_model`` WHERE ``CreatureID`` = $moldeNpc;
UPDATE tmp_prestige_model SET ``CreatureID`` = $npcEntry;
DELETE FROM ``creature_template_model`` WHERE ``CreatureID`` = $npcEntry;
INSERT INTO ``creature_template_model`` SELECT * FROM tmp_prestige_model;
DROP TEMPORARY TABLE tmp_prestige_model;
"@
        try {
            Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                         -Database $m.WorldDb -Sql $sqlModelo | Out-Null
            Write-Ok 'creature_template_model'
        } catch {
            $npcOk = $false
            $npcErro = "$_"
            Write-Warn 'o NPC foi criado mas ficou sem modelo - ele nasce invisivel'
            Write-Info "  $npcErro"
        }
    } elseif ($npcOk) {
        Write-Info 'creature_template_model nao existe neste banco - o modelo veio na propria linha'
    }
}

# ------------------------------------------------------------------- addon --
if (-not $SkipAddon) {
    Write-Step 'Addon do client'
    $addonDestino = Join-Path $settings.ClientDir 'Interface\AddOns\PrestigeUI'
    New-DirectoryIfMissing $addonDestino
    Copy-Item (Join-Path $addonOrig '*') $addonDestino -Recurse -Force
    Write-Ok $addonDestino
    Write-Info 'o addon e cosmetico: sem ele os bonus funcionam, so falta o icone'
}

# ---------------------------------------------------------------- conferir --
Write-Step 'Conferencia'

$instalados = (Get-ChildItem $luaDestino -Filter '*prestige*.lua' -File -ErrorAction SilentlyContinue).Count
Write-Ok "$instalados script(s) do prestigio em lua_scripts"

if (-not $SkipNpc -and $npcOk) {
    $conf = Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                         -Database $m.WorldDb `
                         -Sql "SELECT ``entry``, ``name``, ``npcflag`` FROM ``creature_template`` WHERE ``entry`` = $npcEntry;"
    Write-Host $conf -ForegroundColor DarkGray
} elseif (-not $SkipNpc) {
    Write-Warn 'o NPC NAO foi criado - o resto da instalacao esta no lugar'
    Write-Info 'sem ele nao ha onde clicar para prestigiar; o mod em si esta instalado'
    Write-Info 'crie a mao (ver mods\prestige\README.md) ou rode de novo com'
    Write-Info '  -BaseEntry <entry de um NPC de gossip que exista no seu banco>'
}

Write-Step 'Proximos passos'
Write-Info 'reinicie o worldserver - o ALE carrega os scripts no start'
Write-Info 'o console deve dizer 6 scripts carregados, sem aviso de display id'
if (-not $SkipNpc) { Write-Info "no jogo, com GM ligado:  .npc add $npcEntry" }
Write-Info 'leia mods\prestige\README.md: prestigiar e destrutivo e nao tem desfazer'

# Codigo de saida diferente de zero quando algo ficou faltando: a GUI usa isso
# para nao dizer "instalado" quando so parte entrou.
if (-not $npcOk) { exit 2 }
exit 0
