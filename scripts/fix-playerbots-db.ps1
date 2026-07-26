<#
.SYNOPSIS
    Cria o banco que o fork do Playerbots usa e da acesso ao usuario do servidor.

.DESCRIPTION
    O fork do Playerbots usa um QUARTO banco, alem dos tres do AzerothCore:

        PlayerbotsDatabaseInfo = "127.0.0.1;3306;acore;acore;acore_playerbots"

    Nenhum passo do AzerothCore cria esse banco, entao quem troca o core depois
    de ja ter instalado cai neste erro:

        Could not connect to MySQL database at 127.0.0.1:
        Access denied for user 'acore'@'localhost' (using password: YES)
        DatabasePool Playerbots NOT opened.

    A mensagem fala em senha, mas o problema quase sempre e o banco nao existir:
    sem privilegio sobre um schema inexistente, o MySQL responde "access denied"
    em vez de "unknown database".

    Pede a senha do root, que nao fica salva em lugar nenhum.

.PARAMETER RootPassword
    Senha do root do MySQL. Se nao passar, o script pergunta.

.EXAMPLE
    .\scripts\fix-playerbots-db.ps1
#>
[CmdletBinding()]
param(
    [string]$RootPassword
)

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings
$m = $settings.MySql
$db = $m.PlayerbotsDb

Write-Step "Banco do Playerbots: $db"

if (-not (Test-MySqlReachable -Settings $settings)) {
    Write-Fail "o MySQL nao esta respondendo em $($m.Host):$($m.Port)." `
               'Rode .\scripts\start-mysql.ps1 (como Administrador).'
}

# Se o usuario do servidor ja entra, nao ha o que fazer - e nao ha por que pedir
# a senha do root.
$jaFunciona = $false
try {
    Invoke-MySql -Settings $settings -User $m.User -Password $m.Password -Database $db -Sql 'SELECT 1;' -Quiet | Out-Null
    $jaFunciona = $true
} catch { }

if ($jaFunciona) {
    Write-Ok "'$($m.User)' ja acessa $db - nada a corrigir"
    exit 0
}

Write-Info "'$($m.User)' ainda nao acessa $db"
# ContainsKey e nao "-not $RootPassword": senha vazia e um valor legitimo, e
# com a checagem ingenua ela cairia no Read-Host mesmo tendo sido informada.
if (-not $PSBoundParameters.ContainsKey('RootPassword')) {
    $RootPassword = Read-MySqlRootPassword
}

$sql = New-Object Text.StringBuilder
[void]$sql.AppendLine("CREATE DATABASE IF NOT EXISTS ``$db`` DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_unicode_ci;")

# 'localhost' e '127.0.0.1' sao usuarios DIFERENTES para o MySQL. O erro do
# worldserver cita 'acore'@'localhost', entao os dois precisam do acesso.
foreach ($h in @('localhost', '127.0.0.1')) {
    [void]$sql.AppendLine("CREATE USER IF NOT EXISTS '$($m.User)'@'$h' IDENTIFIED BY '$($m.Password)';")
    [void]$sql.AppendLine("GRANT ALL PRIVILEGES ON ``$db``.* TO '$($m.User)'@'$h' WITH GRANT OPTION;")
}
[void]$sql.AppendLine('FLUSH PRIVILEGES;')

Write-Step 'Criando o banco e liberando o acesso'
Invoke-MySql -Settings $settings -User $m.RootUser -Password $RootPassword -Sql $sql.ToString() | Out-Null

# Conferir de verdade, com o usuario do servidor - que e quem vai conectar.
Invoke-MySql -Settings $settings -User $m.User -Password $m.Password -Database $db -Sql 'SELECT 1;' | Out-Null
Write-Ok "$db criado e acessivel por '$($m.User)'"

Write-Step 'Proximo passo'
Write-Info 'suba o servidor de novo: as tabelas do Playerbots sao criadas sozinhas'
Write-Info 'no primeiro start, igual aos outros bancos.'
exit 0
