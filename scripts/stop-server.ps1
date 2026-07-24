<#
.SYNOPSIS
    Derruba o authserver e o worldserver.

.DESCRIPTION
    Por padrao pede pro worldserver desligar sozinho (fecha a janela do
    console), o que salva os personagens direito. Com -Force, mata o processo.

    NUNCA use -Force com jogadores online sem necessidade: matar o worldserver
    perde o que ainda nao foi salvo no banco.
#>
[CmdletBinding()]
param(
    [switch]$Force
)

. "$PSScriptRoot\lib\common.ps1"

$stopped = @()

foreach ($name in @('worldserver', 'authserver')) {
    $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
    if (-not $procs) {
        Write-Info "$name nao esta rodando"
        continue
    }

    foreach ($p in $procs) {
        if ($Force) {
            Stop-Process -Id $p.Id -Force
            Write-Warn "$name (PID $($p.Id)) morto a forca"
        } else {
            # CloseMainWindow faz o console encerrar limpo
            [void]$p.CloseMainWindow()
            Write-Ok "$name (PID $($p.Id)): pedido de desligamento enviado"
        }
        $stopped += $p
    }
}

if (-not $Force -and $stopped) {
    Write-Step "Esperando encerrar (ate 60s)"
    foreach ($p in $stopped) {
        if (-not $p.WaitForExit(60000)) {
            Write-Warn "$($p.ProcessName) (PID $($p.Id)) nao encerrou sozinho."
            Write-Warn "Se ele travou mesmo, rode: .\scripts\stop-server.ps1 -Force"
        } else {
            Write-Ok "$($p.ProcessName) encerrado"
        }
    }
}

if (-not $stopped) { Write-Host "    Nada rodando." -ForegroundColor Gray }
