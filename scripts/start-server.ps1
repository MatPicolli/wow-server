<#
.SYNOPSIS
    Sobe o authserver e o worldserver.

.DESCRIPTION
    Abre cada servidor na sua propria janela. O worldserver e onde voce digita
    os comandos de GM (account create, .tele, etc).

    No PRIMEIRO start o worldserver aplica todos os SQLs nos bancos vazios.
    Isso demora uns 10 minutos e a janela fica cuspindo linhas de SQL - e
    normal. Espere ate aparecer o prompt 'AC>'.

.PARAMETER AuthOnly
    Sobe so o authserver.

.PARAMETER WorldOnly
    Sobe so o worldserver.
#>
[CmdletBinding()]
param(
    [switch]$AuthOnly,
    [switch]$WorldOnly
)

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings
$server   = $settings.ServerDir
$configs  = Join-Path $server 'configs'

foreach ($exe in @('authserver.exe', 'worldserver.exe')) {
    if (-not (Test-Path (Join-Path $server $exe))) {
        Write-Fail "'$exe' nao existe em '$server'." "Rode 06-deploy.ps1."
    }
}
foreach ($conf in @('authserver.conf', 'worldserver.conf')) {
    if (-not (Test-Path (Join-Path $configs $conf))) {
        Write-Fail "'$conf' nao existe em '$configs'." "Rode 07-configure.ps1."
    }
}

# o MySQL precisa estar de pe antes dos dois
$mysqlSvc = Get-Service -Name 'MySQL*' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($mysqlSvc -and $mysqlSvc.Status -ne 'Running') {
    Write-Step "Iniciando o servico $($mysqlSvc.Name)"
    Start-Service $mysqlSvc.Name
    Write-Ok 'MySQL rodando'
}

function Start-AcProcess {
    param([string]$Exe, [string]$Conf, [string]$Title)

    $running = Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($Exe)) -ErrorAction SilentlyContinue
    if ($running) {
        Write-Warn "$Exe ja esta rodando (PID $($running.Id -join ', ')) - pulando"
        return
    }

    Write-Step "Subindo $Title"
    Start-Process -FilePath (Join-Path $server $Exe) `
                  -ArgumentList @('-c', (Join-Path $configs $Conf)) `
                  -WorkingDirectory $server
    Write-Ok "$Title iniciado em janela separada"
}

if (-not $WorldOnly) {
    Start-AcProcess -Exe 'authserver.exe' -Conf 'authserver.conf' -Title 'authserver (login)'
    Start-Sleep -Seconds 3
}

if (-not $AuthOnly) {
    Start-AcProcess -Exe 'worldserver.exe' -Conf 'worldserver.conf' -Title 'worldserver (mundo)'
}

Write-Host @"

    Os servidores estao subindo nas janelas novas.

    Se for o primeiro start, o worldserver vai levar ~10 min aplicando os SQLs.
    Quando aparecer o prompt 'AC>', crie sua conta na mesma janela:

        account create SEUUSER SUASENHA
        account set gmlevel SEUUSER 3 -1

    Depois rode (uma vez so, com os bancos ja populados):
        .\scripts\08-set-realm-address.ps1

    Pra derrubar tudo:
        .\scripts\stop-server.ps1
"@ -ForegroundColor Gray
