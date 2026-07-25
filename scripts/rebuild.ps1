<#
.SYNOPSIS
    Recompila e reimplanta depois de adicionar, atualizar ou remover modulos.

.DESCRIPTION
    Encadeia o ciclo que voce repete toda vez que mexe em modulos:

        03-build.ps1      recompila (so o que mudou)
        06-deploy.ps1     copia binarios, DLLs e configs dos modulos
        07-configure.ps1  gera os .conf que os modulos trouxeram

    Nao toca no banco nem nos dados do client. O SQL dos modulos e aplicado
    sozinho pelo worldserver no proximo start.

.PARAMETER Clean
    Limpa o cache do CMake antes. Use se um modulo novo nao for detectado.

.PARAMETER Start
    Sobe o servidor ao terminar.

.EXAMPLE
    cd C:\AzerothCore\source\modules
    git clone https://github.com/azerothcore/mod-ah-bot.git
    cd C:\Users\Mateus\Documents\Projetos\AI\wow-server
    .\scripts\rebuild.ps1 -Start
#>
[CmdletBinding()]
param(
    [switch]$Clean,
    [switch]$Start
)

. "$PSScriptRoot\lib\common.ps1"

$settings   = Import-ServerSettings
$modulesDir = Join-Path $settings.SourceDir 'modules'

# --- o que esta instalado --------------------------------------------------
Write-Step "Modulos em $modulesDir"

# Nao exigir CMakeLists.txt na raiz: a maioria dos modulos do AzerothCore nao
# tem um - quem os agrega e o CMakeLists do proprio core. Exigir isso escondia
# quase todos os modulos instalados desta listagem.
$modulos = @()
if (Test-Path $modulesDir) {
    $modulos = Get-ChildItem $modulesDir -Directory -ErrorAction SilentlyContinue |
               Where-Object { $_.Name -notmatch '^\.' }
}

if (-not $modulos) {
    Write-Info 'nenhum modulo instalado'
    Write-Info 'pra adicionar:'
    Write-Info "  cd `"$modulesDir`""
    Write-Info '  git clone https://github.com/azerothcore/mod-ah-bot.git'
} else {
    foreach ($mod in $modulos) {
        $rev = ''
        if (Test-Path (Join-Path $mod.FullName '.git')) {
            try { $rev = (git -C $mod.FullName rev-parse --short HEAD 2>$null | Out-String).Trim() } catch { }

            # Alguns modulos trazem dependencias como submodulo - o Eluna traz
            # a engine Lua assim. Sem inicializar, a compilacao morre com
            # "lua.h: No such file or directory".
            if (Test-Path (Join-Path $mod.FullName '.gitmodules')) {
                Write-Info "$($mod.Name): sincronizando submodulos"
                try {
                    git -C $mod.FullName submodule update --init --recursive 2>&1 | Out-Null
                } catch {
                    Write-Warn "nao consegui atualizar os submodulos de $($mod.Name)"
                }
            }
        }
        Write-Ok ("{0,-32} {1}" -f $mod.Name, $rev)
    }
}

# O CMake so descobre modulo novo quando reconfigura. Ele reconfigura sozinho
# ao ver o diretorio mudar, mas se o modulo nao aparecer na lista do build,
# -Clean resolve.
if ($Clean) { Write-Info 'cache do CMake sera limpo' }

# --- ciclo -----------------------------------------------------------------
$etapas = @(
    @{ N = 'compilando';   Script = '03-build.ps1';    Args = $(if ($Clean) { @{ Clean = $true } } else { @{} }) }
    @{ N = 'implantando';  Script = '06-deploy.ps1';   Args = @{} }
    @{ N = 'configurando'; Script = '07-configure.ps1'; Args = @{} }
)

$inicio = Get-Date
foreach ($e in $etapas) {
    Write-Host ''
    Write-Host "  ---- $($e.N) ----" -ForegroundColor Cyan

    # splat precisa de @ numa variavel: '@($e.Args)' criaria um array com o
    # hashtable dentro, e o script receberia isso como argumento posicional
    $etapaArgs = $e.Args
    & "$PSScriptRoot\$($e.Script)" @etapaArgs
}

$duracao = (Get-Date) - $inicio
Write-Host ''
Write-Ok ("ciclo concluido em {0:hh\:mm\:ss}" -f $duracao)

if ($Start) {
    & "$PSScriptRoot\start-server.ps1"
} else {
    Write-Host @"

    Pra subir:
        .\scripts\start-server.ps1

    No primeiro start depois de adicionar um modulo, o worldserver aplica o
    SQL dele sozinho - a janela vai cuspir linhas de update, e isso e normal.

    Revise os .conf gerados em:
        $($settings.ServerDir)\configs\modules
"@ -ForegroundColor Gray
}
