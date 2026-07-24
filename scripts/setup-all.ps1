<#
.SYNOPSIS
    Roda a instalacao inteira, do build ate a configuracao.

.DESCRIPTION
    Encadeia os scripts 00 a 07. A instalacao das dependencias (01) fica de
    fora porque precisa de Administrador - rode ela antes, uma vez so.

    Isso leva HORAS, quase tudo na geracao dos mmaps. Da pra parar no meio
    (Ctrl+C) e retomar: cada etapa pula o que ja esta feito.

.PARAMETER From
    Comeca a partir de uma etapa (2 a 7). Util pra retomar.

.PARAMETER SkipMmaps
    Nao gera os mmaps. Corta horas do processo, mas os mobs ficam sem
    pathfinding. Da pra gerar depois com:
        .\scripts\05-extract-client-data.ps1 -Only mmaps
#>
[CmdletBinding()]
param(
    [ValidateRange(2, 7)][int]$From = 2,
    [switch]$SkipMmaps,
    [string]$MySqlRootPassword
)

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings
$started  = Get-Date

Write-Host @"

  ===========================================================
   AzerothCore - instalacao completa
  ===========================================================
   fonte:    $($settings.SourceDir)
   build:    $($settings.BuildDir)
   servidor: $($settings.ServerDir)
   client:   $($settings.ClientDir)
  ===========================================================
"@ -ForegroundColor Cyan

# --- 00: pre-requisitos ----------------------------------------------------
Write-Step "Etapa 0/7 - conferindo pre-requisitos"
& "$PSScriptRoot\00-check-prereqs.ps1"
if ($LASTEXITCODE -ne 0) {
    Write-Fail "Faltam pre-requisitos." `
               "Rode como Administrador: .\scripts\01-install-prereqs.ps1  - depois reabra o PowerShell."
}

$steps = @(
    @{ N = 2; Name = 'clonando o codigo-fonte';    Script = '02-clone-source.ps1';        Args = @{} }
    @{ N = 3; Name = 'compilando';                 Script = '03-build.ps1';               Args = @{} }
    @{ N = 4; Name = 'criando os bancos';          Script = '04-setup-database.ps1';      Args = @{} }
    @{ N = 5; Name = 'extraindo dados do client';  Script = '05-extract-client-data.ps1'; Args = @{} }
    @{ N = 6; Name = 'montando o servidor';        Script = '06-deploy.ps1';              Args = @{} }
    @{ N = 7; Name = 'configurando';               Script = '07-configure.ps1';           Args = @{} }
)

foreach ($step in $steps) {
    if ($step.N -lt $From) {
        Write-Info "etapa $($step.N) pulada (-From $From)"
        continue
    }

    $stepArgs = $step.Args.Clone()
    if ($step.N -eq 4 -and $MySqlRootPassword) { $stepArgs['RootPassword'] = $MySqlRootPassword }
    if ($step.N -eq 5 -and $SkipMmaps)         { $stepArgs['SkipMmaps']    = $true }

    Write-Host ''
    Write-Host "  ---- Etapa $($step.N)/7 - $($step.Name) ----" -ForegroundColor Cyan
    & "$PSScriptRoot\$($step.Script)" @stepArgs
}

$elapsed = (Get-Date) - $started

Write-Host @"

  ===========================================================
   Instalacao concluida em $("{0:hh\:mm\:ss}" -f $elapsed)
  ===========================================================

   1) Suba o servidor:
        .\scripts\start-server.ps1

   2) Espere o worldserver popular os bancos (~10 min no 1o start),
      ate aparecer 'AC>'. Entao crie sua conta nessa janela:

        account create SEUUSER SUASENHA
        account set gmlevel SEUUSER 3 -1

   3) Ajuste o endereco do realm:
        .\scripts\08-set-realm-address.ps1

   4) Abra o Wow.exe do client e logue com a conta criada.

   Detalhes e comandos de GM: docs\pos-instalacao.md
"@ -ForegroundColor Green
