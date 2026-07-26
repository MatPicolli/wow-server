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

.PARAMETER Force
    Compila mesmo com um modulo de fork sobre o core errado. So use se souber
    exatamente o que esta fazendo - o normal e que a compilacao falhe.

.EXAMPLE
    cd C:\AzerothCore\source\modules
    git clone https://github.com/azerothcore/mod-ah-bot.git
    cd C:\Users\Mateus\Documents\Projetos\AI\wow-server
    .\scripts\rebuild.ps1 -Start
#>
[CmdletBinding()]
param(
    [switch]$Clean,
    [switch]$Start,
    [switch]$Force
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

            # Alguns modulos trazem dependencias como submodulo. Sem
            # inicializar, a pasta da dependencia fica vazia e a compilacao
            # morre num include que nao existe.
            $gitmodules = Join-Path $mod.FullName '.gitmodules'
            if (Test-Path $gitmodules) {
                Write-Info "$($mod.Name): sincronizando submodulos"

                # 2>&1 sob ErrorActionPreference='Stop' transforma stderr em erro
                # terminante antes de podermos ler $LASTEXITCODE - por isso a
                # preferencia e relaxada so aqui.
                $saidaSub = ''
                $codigoSub = 0
                try {
                    $anterior = $ErrorActionPreference
                    $ErrorActionPreference = 'Continue'
                    $saidaSub = (git -C $mod.FullName submodule update --init --recursive 2>&1 | Out-String).Trim()
                    $codigoSub = $LASTEXITCODE
                } catch {
                    $codigoSub = 1
                    $saidaSub = "$_"
                } finally {
                    $ErrorActionPreference = $anterior
                }

                # Falha silenciosa aqui custa uma compilacao inteira: o modulo
                # aparece instalado, mas o codigo da dependencia nao esta la e o
                # erro so surge 20 minutos depois como "lua.h: No such file".
                if ($codigoSub -ne 0) {
                    Write-Warn "nao consegui atualizar os submodulos de $($mod.Name)"
                    foreach ($linha in ($saidaSub -split "`r?`n")) {
                        if ($linha.Trim()) { Write-Info "  $linha" }
                    }
                }

                foreach ($vazio in (Get-EmptySubmodulePath $mod.FullName)) {
                    Write-Warn "$($mod.Name): a dependencia '$vazio' esta vazia - a compilacao vai falhar"
                    Write-Info  "  para resolver: git -C `"$($mod.FullName)`" submodule update --init --recursive"
                }
            }
        }
        Write-Ok ("{0,-32} {1}" -f $mod.Name, $rev)
    }
}

# --- pasta com o nome que o core nao reconhece mais -------------------------
# Custou uma compilacao inteira: com a pasta chamada 'mod-eluna' o core nunca
# entra no ramo que poe o lua.h no include path, a lib Lua compila, e so os
# arquivos do modulo falham - 25 erros C1083 iguais, 20 minutos depois.
foreach ($ren in (Get-RenamedModule $modulesDir)) {
    if ($ren.Conflito) {
        $msg = "modules\$($ren.Atual) e modules\$($ren.Correto) existem os dois - o mesmo codigo entraria duas vezes na compilacao"
        $comoResolver = ".\scripts\remove-module.ps1 -Name $($ren.Atual) -Apply"
    } else {
        $msg = "modules\$($ren.Atual) precisa se chamar '$($ren.Correto)': e por esse nome que o core liga a biblioteca Lua ao alvo dos modulos"
        $comoResolver = "Rename-Item `"$(Join-Path $modulesDir $ren.Atual)`" '$($ren.Correto)'"
    }

    if ($Force) {
        Write-Warn $msg
        Write-Warn '-Force: seguindo mesmo assim, a compilacao provavelmente vai falhar'
    } else {
        Write-Fail $msg "rode: $comoResolver  (ou de novo com -Force)"
    }
}

# --- modulo de fork sobre o core errado ------------------------------------
# mod-playerbots e mod-ah-bot-style forks compilam contra simbolos que so
# existem no fork. Sobre o core oficial isso rende centenas de C2660/C2039 -
# duas compilacoes perdidas ate esta checagem existir.
# O primeiro endereco e o canonico (usado nas mensagens); os demais sao nomes
# antigos que o GitHub redireciona - o Playerbots migrou de conta pessoal para
# organizacao, e quem clonou pelo endereco velho tem exatamente o mesmo core.
$forks = @{
    'mod-playerbots' = @(
        'https://github.com/mod-playerbots/azerothcore-wotlk'
        'https://github.com/liyunfan1223/azerothcore-wotlk'
    )
}

$origemCore = ''
if (Test-Path (Join-Path $settings.SourceDir '.git')) {
    try { $origemCore = (git -C $settings.SourceDir remote get-url origin 2>$null | Out-String).Trim() } catch { }
}

function Test-MesmoRepo {
    <#
        Compara so o final 'dono/repo' da URL, nao a URL inteira.

        A mesma origem aparece escrita de varias formas - https, ssh
        (git@github.com:dono/repo.git), com credencial embutida, atras de um
        proxy corporativo. Comparar a URL crua acusaria core errado em todos
        esses casos e travaria uma compilacao perfeitamente valida.

        A barra final vem depois do .git ('...repo.git/'), entao ela sai antes -
        na ordem inversa o sufixo nao casa.
    #>
    param([string]$A, [string]$B)

    $n = {
        param($u)
        $limpo = ($u.Trim().TrimEnd('/')) -replace '\.git$', ''
        $partes = $limpo.TrimEnd('/') -split '[/:]' | Where-Object { $_ }
        if ($partes.Count -ge 2) {
            return (($partes[-2] + '/' + $partes[-1])).ToLowerInvariant()
        }
        return $limpo.ToLowerInvariant()
    }

    return (& $n $A) -eq (& $n $B)
}

foreach ($nome in $forks.Keys) {
    if (-not ($modulos | Where-Object { $_.Name -eq $nome })) { continue }
    if (-not $origemCore) { continue }

    $aceitos = @($forks[$nome])
    $serve = $false
    foreach ($url in $aceitos) { if (Test-MesmoRepo $origemCore $url) { $serve = $true; break } }
    if ($serve) { continue }

    $msg = "$nome exige o core de $($aceitos[0]), mas o codigo em $($settings.SourceDir) veio de $origemCore"
    if ($Force) {
        Write-Warn $msg
        Write-Warn '-Force: seguindo mesmo assim, a compilacao provavelmente vai falhar'
    } else {
        Write-Fail $msg 'na GUI, aba Modulos, use o botao "Corrigir o core" no card do Playerbots (ou rode de novo com -Force)'
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
