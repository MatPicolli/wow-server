<#
.SYNOPSIS
    Troca o codigo-fonte do servidor por um fork, preservando o que da trabalho
    refazer.

.DESCRIPTION
    Alguns "modulos" nao sao modulos: Playerbots e NPCBots exigem um core
    diferente. Sobre o core oficial eles compilam contra simbolos que nao
    existem e falham com centenas de erros C2660.

    Este script troca a origem do codigo-fonte e reconstroi a arvore. Como a
    pasta modules/ mora dentro do codigo-fonte, os modulos instalados sao
    anotados antes e reclonados depois.

    O QUE E PRESERVADO
        - os dados extraidos do client (dbc, maps, vmaps, mmaps) - as horas de
          extracao NAO se repetem
        - o banco: contas, personagens, tudo
        - o config/settings.psd1, alterado so nas duas chaves da origem

    O QUE E REFEITO
        - o clone do codigo-fonte (alguns minutos)
        - a compilacao (cerca de 20 minutos)

    Preview por padrao. Sem -Apply nada e alterado.

.PARAMETER Playerbots
    Atalho para o fork do Playerbots. Equivale a informar -Repository e -Branch.

.PARAMETER Oficial
    Volta para o AzerothCore oficial. Use para desistir do Playerbots.

.PARAMETER Repository
    URL do fork, quando nao for um dos atalhos acima.

.PARAMETER Branch
    Branch do fork.

.PARAMETER Apply
    Executa de verdade. Sem isso o script so mostra o que faria.

.PARAMETER SkipBackup
    Nao faz backup do banco antes. O padrao e fazer.

.NOTES
    Codigos de saida:
        0  tudo certo (ou so uma previa)
        1  falhou
        3  o MySQL nao esta no ar, entao nao houve backup; nada foi alterado.
           Repita com -SkipBackup para seguir mesmo assim.

.PARAMETER NoBuild
    Troca o core e para, sem recompilar. Util para encadear outra coisa antes.

.EXAMPLE
    .\scripts\switch-core.ps1 -Playerbots
    .\scripts\switch-core.ps1 -Playerbots -Apply

.EXAMPLE
    # desistir do Playerbots e voltar ao core oficial
    .\scripts\switch-core.ps1 -Oficial -Apply
#>
[CmdletBinding()]
param(
    [switch]$Playerbots,
    [switch]$Oficial,
    [string]$Repository,
    [string]$Branch,
    [switch]$Apply,
    [switch]$SkipBackup,
    [switch]$NoBuild
)

. "$PSScriptRoot\lib\common.ps1"

# --- para onde vamos ---------------------------------------------------------
if ($Playerbots) {
    $Repository = 'https://github.com/mod-playerbots/azerothcore-wotlk.git'
    $Branch     = 'Playerbot'
} elseif ($Oficial) {
    $Repository = 'https://github.com/azerothcore/azerothcore-wotlk.git'
    $Branch     = 'master'
}

if (-not $Repository -or -not $Branch) {
    Write-Fail 'informe para onde trocar.' `
               'Use -Playerbots, -Oficial, ou -Repository <url> -Branch <nome>.'
}

$settings  = Import-ServerSettings
$sourceDir = $settings.SourceDir
$modulesDir = Join-Path $sourceDir 'modules'

Write-Step 'Troca do codigo-fonte do servidor'

# --- ja estamos la? ----------------------------------------------------------
function Get-RepoTail {
    # Compara so o final 'dono/repo': a mesma origem aparece como https, ssh,
    # com credencial embutida ou atras de proxy.
    param([string]$Url)
    $limpo = ($Url.Trim().TrimEnd('/')) -replace '\.git$', ''
    $partes = $limpo.TrimEnd('/') -split '[/:]' | Where-Object { $_ }
    if ($partes.Count -ge 2) { return "$($partes[-2])/$($partes[-1])".ToLowerInvariant() }
    return $limpo.ToLowerInvariant()
}

$origemAtual = ''
if (Test-Path (Join-Path $sourceDir '.git')) {
    try { $origemAtual = (git -C $sourceDir remote get-url origin 2>$null | Out-String).Trim() } catch { }
}

$branchAtual = ''
if ($origemAtual) {
    try { $branchAtual = (git -C $sourceDir rev-parse --abbrev-ref HEAD 2>$null | Out-String).Trim() } catch { }
}

# O que vale e o que esta no disco, nao o que o settings.psd1 diz. Os dois
# podem discordar - por exemplo quando um clone anterior falhou no meio - e
# esconder isso levaria a decidir pela informacao errada.
Write-Info "settings.psd1:  $($settings.SourceBranch) @ $($settings.SourceRepository)"
if ($origemAtual) {
    Write-Info "no disco:       $branchAtual @ $origemAtual"
} else {
    Write-Info "no disco:       nao ha clone em $sourceDir"
}
Write-Info "destino:        $Branch @ $Repository"

if ($origemAtual -and (Get-RepoTail $origemAtual) -eq (Get-RepoTail $Repository) -and $branchAtual -eq $Branch) {
    Write-Ok 'o codigo-fonte no disco ja e esse'

    # As configuracoes podem ter ficado para tras; sem elas alinhadas, o
    # rebuild.ps1 continua acusando core errado.
    if ((Get-RepoTail $settings.SourceRepository) -ne (Get-RepoTail $Repository) -or
        $settings.SourceBranch -ne $Branch) {

        if (-not $Apply) {
            Write-Warn 'so o settings.psd1 esta desatualizado'
            Write-Info 'rode de novo com -Apply para alinhar (nao reclona nem recompila)'
            exit 0
        }

        Write-Step 'Alinhando o settings.psd1 com o que ja esta no disco'
        Set-ServerSetting -Key SourceRepository -Value $Repository
        Set-ServerSetting -Key SourceBranch     -Value $Branch
        Write-Ok 'settings.psd1 atualizado - nada foi reclonado'
        exit 0
    }

    Write-Info 'nada a fazer; se a compilacao ainda falha, rode .\scripts\rebuild.ps1'
    exit 0
}

# --- o que sera reinstalado --------------------------------------------------
$instalados = @()
if (Test-Path $modulesDir) {
    foreach ($dir in (Get-ChildItem $modulesDir -Directory -ErrorAction SilentlyContinue)) {
        if (-not (Test-Path (Join-Path $dir.FullName '.git'))) {
            Write-Warn "$($dir.Name) nao e um clone git - sera PERDIDO na troca"
            continue
        }
        $url = ''
        try { $url = (git -C $dir.FullName remote get-url origin 2>$null | Out-String).Trim() } catch { }
        if ($url) {
            $instalados += [pscustomobject]@{ Nome = $dir.Name; Url = $url }
        } else {
            Write-Warn "nao descobri a origem de $($dir.Name) - sera PERDIDO na troca"
        }
    }
}

Write-Step 'Modulos que serao reclonados depois'
if ($instalados.Count -eq 0) {
    Write-Info 'nenhum'
} else {
    foreach ($m in $instalados) { Write-Info ("{0,-30} {1}" -f $m.Nome, $m.Url) }
}

Write-Step 'Resumo'
Write-Info 'preservado:  dados extraidos do client, banco de dados, configuracoes'
Write-Info 'refeito:     clone do codigo-fonte e compilacao (~20 min no total)'
Write-Info "apagado:     $sourceDir"

if (-not $Apply) {
    Write-Step 'Isto foi so uma previa'
    Write-Info 'para executar de verdade, repita o comando com  -Apply'
    exit 0
}

# --- backup ------------------------------------------------------------------
# Nada aqui toca no banco, mas a compilacao seguinte roda um worldserver novo
# que aplica SQL. Um backup antes custa segundos e ja salvou o dia.
if ($SkipBackup) {
    Write-Warn 'pulando o backup do banco a seu pedido'
} elseif (-not (Test-MySqlReachable -Settings $settings)) {
    # Um MySQL desligado nao e motivo para desistir da troca - ela nao encosta
    # no banco. Mas seguir calado seria prometer um backup que nao existe.
    Write-Step 'Backup do banco'
    Write-Warn "o MySQL nao esta respondendo em $($settings.MySql.Host):$($settings.MySql.Port)"
    Write-Info 'a troca do core NAO mexe no banco: seus personagens e contas ficam onde estao.'
    Write-Info 'o backup e precaucao para a compilacao seguinte, quando o worldserver aplica SQL.'
    Write-Info ''
    Write-Info 'escolha uma:'
    Write-Info '  1) ligue o MySQL e rode este comando de novo (recomendado):'
    Write-Info '       .\scripts\start-mysql.ps1 -Automatic     (como Administrador)'
    Write-Info '  2) siga sem backup:'
    Write-Info '       .\scripts\switch-core.ps1 -Playerbots -Apply -SkipBackup'
    Write-Info ''
    Write-Host '    [erro] sem MySQL no ar nao da para fazer backup. Nada foi alterado.' -ForegroundColor Red

    # Codigo proprio, e nao Write-Fail: assim a GUI distingue "falhou por causa
    # do backup" de qualquer outra falha, e pode oferecer o -SkipBackup - la ela
    # nao tem como o usuario reescrever a linha de comando.
    exit 3
} else {
    Write-Step 'Backup do banco antes de mexer'
    & "$PSScriptRoot\backup-db.ps1"
    if ($LASTEXITCODE -ne 0) {
        Write-Fail 'o backup falhou.' 'Resolva, ou repita com -SkipBackup se tiver certeza.'
    }
}

# --- trocar a origem ---------------------------------------------------------
Write-Step 'Gravando a nova origem em settings.psd1'
Set-ServerSetting -Key SourceRepository -Value $Repository
Set-ServerSetting -Key SourceBranch     -Value $Branch
Write-Ok 'settings.psd1 atualizado'

# --- reclonar ----------------------------------------------------------------
Write-Step 'Reclonando o codigo-fonte'
& "$PSScriptRoot\02-clone-source.ps1" -Force
if ($LASTEXITCODE -ne 0) {
    Write-Fail 'o clone do novo core falhou.' `
               "As configuracoes ja apontam para $Branch @ $Repository; corrija a rede e rode este script de novo."
}

# --- devolver os modulos -----------------------------------------------------
if ($instalados.Count -gt 0) {
    Write-Step 'Reinstalando os modulos'
    New-DirectoryIfMissing $modulesDir

    foreach ($m in $instalados) {
        $destino = Join-Path $modulesDir $m.Nome
        if (Test-Path $destino) { Write-Info "$($m.Nome) ja esta la"; continue }

        Write-Info "clonando $($m.Nome)"
        # --recurse-submodules: sem isso o Eluna vem sem a engine Lua e a
        # compilacao morre com "lua.h: No such file or directory".
        $anterior = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        git clone --recurse-submodules $m.Url $destino 2>&1 | ForEach-Object { Write-Info "  $_" }
        $codigo = $LASTEXITCODE
        $ErrorActionPreference = $anterior

        if ($codigo -ne 0) { Write-Warn "nao consegui reclonar $($m.Nome) - reinstale pela GUI depois" }
        else { Write-Ok $m.Nome }
    }
}

if ($NoBuild) {
    Write-Step 'Core trocado'
    Write-Info 'a compilacao NAO foi feita (-NoBuild). Rode .\scripts\rebuild.ps1 quando quiser.'
    exit 0
}

# --- compilar ----------------------------------------------------------------
Write-Step 'Recompilando com o core novo'
& "$PSScriptRoot\rebuild.ps1"
if ($LASTEXITCODE -ne 0) { Write-Fail 'a compilacao falhou apos a troca do core.' }

Write-Step 'Pronto'
Write-Info 'suba o servidor com .\scripts\start-server.ps1'
exit 0
