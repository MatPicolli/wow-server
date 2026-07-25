<#
.SYNOPSIS
    Liga o servico do MySQL.

.DESCRIPTION
    O MySQL instalado pelo winget costuma ficar com inicializacao manual, entao
    ele nao sobe sozinho depois de reiniciar o Windows. Sem ele no ar, o
    backup falha, o worldserver nao conecta e o 04-setup-database.ps1 tambem
    nao.

    Iniciar servico exige Administrador. Sem privilegio, este script diz o que
    fazer em vez de falhar com "Acesso negado".

.PARAMETER Automatic
    Alem de iniciar, marca o servico para subir junto com o Windows - assim o
    problema nao volta no proximo boot.

.PARAMETER Status
    So mostra a situacao, sem iniciar nada. Nao precisa de Administrador.

.EXAMPLE
    .\scripts\start-mysql.ps1 -Status
    .\scripts\start-mysql.ps1
    .\scripts\start-mysql.ps1 -Automatic
#>
[CmdletBinding()]
param(
    [switch]$Automatic,
    [switch]$Status
)

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings
$m = $settings.MySql

Write-Step 'Servico do MySQL'

# O nome varia com a versao: MySQL80, MySQL84, MySQL91... Procurar pelo prefixo
# evita ter que adivinhar.
$servicos = @(Get-Service -Name 'MySQL*' -ErrorAction SilentlyContinue)

if ($servicos.Count -eq 0) {
    Write-Warn 'nao achei nenhum servico com nome comecando por "MySQL"'
    Write-Info 'se o MySQL esta instalado mas sem servico, o instalador nao chegou a'
    Write-Info 'configurar o servidor. Abra o "MySQL Installer" e rode o Reconfigure.'
    Write-Info ''
    Write-Info 'para conferir se ha algo escutando mesmo assim:'
    Write-Info "  Test-NetConnection $($m.Host) -Port $($m.Port)"
    exit 1
}

foreach ($s in $servicos) {
    Write-Info ("{0,-16} {1,-10} inicializacao: {2}" -f $s.Name, $s.Status, $s.StartType)
}

$svc = $servicos | Where-Object { $_.Status -eq 'Running' } | Select-Object -First 1
if (-not $svc) { $svc = $servicos[0] }

# A porta e o que realmente importa: o servico pode estar 'Running' e o servidor
# escutando em outro endereco, ou nem ter subido de verdade ainda.
$noAr = Test-MySqlReachable -Settings $settings

if ($Status) {
    if ($noAr) { Write-Ok "respondendo em $($m.Host):$($m.Port)" }
    else       { Write-Warn "nada respondendo em $($m.Host):$($m.Port)" }
    exit 0
}

if ($svc.Status -eq 'Running' -and $noAr) {
    Write-Ok "$($svc.Name) ja esta rodando e respondendo em $($m.Host):$($m.Port)"
} elseif ($svc.Status -ne 'Running') {
    # Assert-Admin so aqui: exigir elevacao para depois descobrir que nao havia
    # nada a fazer e pedir o que nao precisa.
    Assert-Admin

    Write-Step "Iniciando $($svc.Name)"
    try {
        Start-Service $svc.Name -ErrorAction Stop
    } catch {
        Write-Fail "nao consegui iniciar $($svc.Name): $_" `
                   'Abra "Servicos" (services.msc), procure o MySQL e veja o erro de la.'
    }

    # Start-Service volta assim que o servico reporta 'Running', o que acontece
    # antes de o servidor aceitar conexao. Quem manda e a porta.
    $limite = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $limite -and -not (Test-MySqlReachable -Settings $settings)) {
        Start-Sleep -Milliseconds 500
    }

    if (Test-MySqlReachable -Settings $settings) {
        Write-Ok "MySQL respondendo em $($m.Host):$($m.Port)"
    } else {
        Write-Fail "o servico subiu mas nada responde em $($m.Host):$($m.Port)." `
                   "Confira MySql.Host e MySql.Port em config\settings.psd1, ou veja o log de erro do MySQL."
    }
} else {
    # Servico 'Running' e porta muda: quase sempre endereco ou porta errados no
    # settings.psd1. Reiniciar o servico nao resolveria, entao nem tentamos.
    Write-Fail "$($svc.Name) esta rodando, mas nada responde em $($m.Host):$($m.Port)." `
               "Confira MySql.Host e MySql.Port em config\settings.psd1. Se o MySQL usa outra porta, e la que esta a diferenca."
}

if ($Automatic) {
    if ($svc.StartType -eq 'Automatic') {
        Write-Ok 'ja sobe junto com o Windows'
    } else {
        Assert-Admin
        Write-Step 'Deixando o MySQL subir junto com o Windows'
        try {
            Set-Service -Name $svc.Name -StartupType Automatic -ErrorAction Stop
            Write-Ok "$($svc.Name) agora inicia sozinho"
        } catch {
            Write-Warn "nao consegui mudar a inicializacao: $_"
        }
    }
}

exit 0
