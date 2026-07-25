<#
.SYNOPSIS
    Apaga o que e refazivel para reinstalar do zero, preservando os dados
    extraidos do client.

.DESCRIPTION
    Os dados extraidos (dbc, maps, vmaps, mmaps) levam horas para gerar e sao
    identicos toda vez - o client nao muda. Tudo o mais e barato de refazer.

    A armadilha: esses dados ficam em <ServerDir>\Data, ou seja, DENTRO da
    mesma pasta que guarda os binarios. Apagar a pasta do servidor inteira,
    que e o instinto natural, joga fora justamente as horas.

    Este script apaga o resto e nao encosta em Data\.

    APAGA
        <SourceDir>            clone do AzerothCore, com modules/ dentro
        <BuildDir>             cache do CMake e objetos da compilacao
        <ServerDir>\*          binarios, DLLs e logs
        <ServerDir>\configs    salvo antes em configs.bak-<data>

    PRESERVA
        <ServerDir>\Data       dbc, maps, vmaps, mmaps, Cameras
        o client do WoW
        config\settings.psd1
        os bancos, a menos que -IncludeDatabase

    Preview por padrao. Sem -Apply nada e apagado.

.PARAMETER Apply
    Apaga de verdade.

.PARAMETER IncludeDatabase
    Tambem derruba acore_auth, acore_characters e acore_world - ou seja, apaga
    contas e personagens. Faz backup antes.

.PARAMETER KeepModules
    Anota os modulos instalados e reclona depois de apagar o codigo-fonte.

.EXAMPLE
    .\scripts\reset-server.ps1
    .\scripts\reset-server.ps1 -Apply -KeepModules
    .\scripts\setup-all.ps1

.EXAMPLE
    # recomeco completo, personagens inclusive
    .\scripts\reset-server.ps1 -Apply -IncludeDatabase
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$IncludeDatabase,
    [switch]$KeepModules
)

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings
$source   = $settings.SourceDir
$build    = $settings.BuildDir
$server   = $settings.ServerDir
$dataDir  = Join-Path $server 'Data'

function Get-TamanhoGB {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return 0 }
    try {
        $soma = (Get-ChildItem $Path -Recurse -File -Force -ErrorAction SilentlyContinue |
                 Measure-Object Length -Sum).Sum
        if (-not $soma) { return 0 }   # pasta vazia devolve $null, nao 0
        return [math]::Round(($soma / 1GB), 2)
    } catch { return 0 }
}

Write-Step 'O que sera preservado'

if (Test-Path $dataDir) {
    $tam = Get-TamanhoGB $dataDir
    Write-Ok ("dados extraidos do client: {0} ({1} GB)" -f $dataDir, $tam)
    foreach ($sub in @('dbc', 'maps', 'vmaps', 'mmaps', 'Cameras')) {
        $p = Join-Path $dataDir $sub
        if (Test-Path $p) {
            # @() obrigatorio: com um unico arquivo o Get-ChildItem devolve o
            # objeto solto, e .Count lanca sob StrictMode.
            $qtd = @(Get-ChildItem $p -Recurse -File -Force -ErrorAction SilentlyContinue).Count
            Write-Info ("  {0,-10} {1} arquivos" -f $sub, $qtd)
        } else {
            Write-Warn "  $sub nao esta la"
        }
    }
} else {
    # Sem isso, o ponto inteiro do script deixa de existir - melhor dizer agora
    # do que o usuario descobrir depois de apagar tudo.
    Write-Warn "nao ha dados extraidos em $dataDir"
    Write-Info 'a etapa 5 (extracao) vai rodar inteira depois - vao ser horas'
}

Write-Info "client do WoW:  $($settings.ClientDir)"
if (-not $IncludeDatabase) { Write-Info 'bancos de dados: preservados (contas e personagens intactos)' }

Write-Step 'O que sera apagado'

$alvos = @()
if (Test-Path $source) { $alvos += [pscustomobject]@{ O = 'codigo-fonte'; Caminho = $source; GB = (Get-TamanhoGB $source) } }
if (Test-Path $build)  { $alvos += [pscustomobject]@{ O = 'compilacao';   Caminho = $build;  GB = (Get-TamanhoGB $build) } }

# Dentro de ServerDir vai item por item, pulando Data\. Apagar a pasta toda e
# exatamente o erro que este script existe para evitar.
$itensServidor = @()
if (Test-Path $server) {
    foreach ($item in (Get-ChildItem $server -Force -ErrorAction SilentlyContinue)) {
        if ($item.Name -eq 'Data') { continue }
        $itensServidor += $item
    }
}

foreach ($i in $itensServidor) {
    $alvos += [pscustomobject]@{
        O = 'servidor'
        Caminho = $i.FullName
        GB = if ($i.PSIsContainer) { Get-TamanhoGB $i.FullName } else { [math]::Round($i.Length / 1GB, 2) }
    }
}

if ($alvos.Count -eq 0) {
    Write-Info 'nada a apagar - ja esta limpo'
} else {
    foreach ($a in $alvos) { Write-Info ("{0,-14} {1,7:N2} GB  {2}" -f $a.O, $a.GB, $a.Caminho) }
    Write-Info ('total: {0:N2} GB' -f (($alvos | Measure-Object GB -Sum).Sum))
}

# --- modulos -----------------------------------------------------------------
$modulos = @()
$modulesDir = Join-Path $source 'modules'
if (Test-Path $modulesDir) {
    foreach ($dir in (Get-ChildItem $modulesDir -Directory -ErrorAction SilentlyContinue)) {
        $url = ''
        if (Test-Path (Join-Path $dir.FullName '.git')) {
            try { $url = (git -C $dir.FullName remote get-url origin 2>$null | Out-String).Trim() } catch { }
        }
        $modulos += [pscustomobject]@{ Nome = $dir.Name; Url = $url }
    }
}

if ($modulos.Count -gt 0) {
    Write-Step 'Modulos instalados'
    foreach ($m in $modulos) {
        if ($m.Url) {
            $marca = if ($KeepModules) { '[sera reclonado]' } else { '[sera perdido]' }
            Write-Info ("{0,-30} {1} {2}" -f $m.Nome, $marca, $m.Url)
        } else {
            Write-Warn "$($m.Nome) nao e clone git - nao ha como reclonar"
        }
    }
    if (-not $KeepModules) { Write-Info 'use -KeepModules para reinstala-los automaticamente' }
}

if ($IncludeDatabase) {
    Write-Step 'Bancos de dados'
    Write-Warn 'contas e personagens serao APAGADOS'
    Write-Info "  $($settings.MySql.AuthDb), $($settings.MySql.CharDb), $($settings.MySql.WorldDb)"
    Write-Info 'sera feito um backup antes'
}

if (-not $Apply) {
    Write-Step 'Isto foi so uma previa'
    Write-Info 'para apagar de verdade, repita com  -Apply'
    exit 0
}

# --- backup do banco ---------------------------------------------------------
if ($IncludeDatabase) {
    if (-not (Test-MySqlReachable -Settings $settings)) {
        Write-Fail "o MySQL nao esta respondendo em $($settings.MySql.Host):$($settings.MySql.Port)." `
                   'Rode .\scripts\start-mysql.ps1 (como Administrador). Nada foi apagado.'
    }

    Write-Step 'Backup antes de derrubar os bancos'
    & "$PSScriptRoot\backup-db.ps1" -IncludeWorld
    if ($LASTEXITCODE -ne 0) { Write-Fail 'o backup falhou.' 'Nada foi apagado.' }
}

# --- salvar os configs -------------------------------------------------------
# Sao regerados pelo 07-configure.ps1, mas quem editou taxas ou o nome do realm
# na mao perderia isso sem aviso. Copiar custa nada.
$configs = Join-Path $server 'configs'
if (Test-Path $configs) {
    $bak = Join-Path $server ("configs.bak-" + (Get-Date -Format 'yyyy-MM-dd_HHmm'))
    Write-Step 'Guardando os configs atuais'
    try {
        Copy-Item $configs $bak -Recurse -Force
        Write-Ok "copiados para $bak"
    } catch {
        Write-Warn "nao consegui copiar os configs: $_"
    }
}

# --- apagar ------------------------------------------------------------------
Write-Step 'Apagando'

function Remove-Arvore {
    param([string]$Path, [string]$Rotulo)
    if (-not (Test-Path $Path)) { return }

    # Objetos do git vem somente-leitura e o Remove-Item para no primeiro deles,
    # deixando a pasta pela metade.
    try {
        Get-ChildItem $Path -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
            try { $_.Attributes = [IO.FileAttributes]::Normal } catch { }
        }
        Remove-Item $Path -Recurse -Force -ErrorAction Stop
        Write-Ok "$Rotulo apagado"
    } catch {
        Write-Fail "nao consegui apagar $Path`: $_" `
                   'Pare o servidor (.\scripts\stop-server.ps1) e feche editores abertos nessa pasta.'
    }
}

Remove-Arvore -Path $source -Rotulo 'codigo-fonte'
Remove-Arvore -Path $build  -Rotulo 'compilacao'

foreach ($i in $itensServidor) {
    # o backup dos configs acabou de ser criado aqui dentro; nao apagar
    if ($i.Name -like 'configs.bak-*') { continue }
    Remove-Arvore -Path $i.FullName -Rotulo $i.Name
}

# Conferencia final: se Data sumiu, algo saiu muito errado e o usuario precisa
# saber agora, nao depois de rodar setup-all por meia hora.
if (Test-Path $dataDir) {
    Write-Ok "Data\ intacto ($(Get-TamanhoGB $dataDir) GB)"
} else {
    Write-Warn 'Data\ nao esta mais la - a extracao vai ter que rodar de novo'
}

# --- bancos ------------------------------------------------------------------
if ($IncludeDatabase) {
    Write-Step 'Derrubando os bancos'
    $m = $settings.MySql
    foreach ($db in @($m.AuthDb, $m.CharDb, $m.WorldDb)) {
        Invoke-MySql -Settings $settings -User $m.RootUser -Password $m.RootPassword `
                     -Sql "DROP DATABASE IF EXISTS ``$db``;" | Out-Null
        Write-Ok "$db removido"
    }
}

# --- modulos -----------------------------------------------------------------
if ($KeepModules -and $modulos.Count -gt 0) {
    Write-Step 'Guardando a lista de modulos'
    $lista = Join-Path $settings.Root 'modules-a-reinstalar.txt'
    $texto = ($modulos | Where-Object { $_.Url } | ForEach-Object { $_.Url }) -join "`r`n"
    Write-TextFileNoBom -Path $lista -Content $texto
    Write-Ok "lista salva em $lista"
    Write-Info 'depois do 02-clone-source.ps1, reclone com:'
    Write-Info "  Get-Content `"$lista`" | ForEach-Object { git clone --recurse-submodules `$_ (Join-Path `"$modulesDir`" (Split-Path `$_ -Leaf)) }"
}

Write-Step 'Pronto'
Write-Info 'para remontar o servidor:'
Write-Info '  .\scripts\setup-all.ps1'
Write-Info ''
Write-Info 'a etapa 5 (extracao) vai ver os dados em Data\ e passar em segundos.'
exit 0
