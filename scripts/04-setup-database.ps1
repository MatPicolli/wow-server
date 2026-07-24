<#
.SYNOPSIS
    Cria o usuario e os tres bancos que o AzerothCore usa.

.DESCRIPTION
    Faz o equivalente ao data/sql/create/create_mysql.sql oficial, mas usando o
    usuario/senha/nomes de banco que estao no seu config\settings.psd1.

    Os bancos ficam VAZIOS aqui. Quem popula e o proprio worldserver no primeiro
    start (o "auto updater" baixa e aplica os SQLs). Isso e esperado.

.PARAMETER RootPassword
    Senha do root do MySQL. Se nao passar, o script pergunta sem exibir na tela.
#>
[CmdletBinding()]
param(
    [string]$RootPassword
)

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings
$m        = $settings.MySql

if (-not (Get-MySqlExe)) {
    Write-Fail "mysql.exe nao encontrado." "Instale o MySQL e coloque a pasta bin\ no PATH."
}

Write-Step "Conectando no MySQL em $($m.Host):$($m.Port)"
if (-not $RootPassword) { $RootPassword = Read-MySqlRootPassword }

try {
    Invoke-MySql -Settings $settings -User $m.RootUser -Password $RootPassword -Sql 'SELECT VERSION();' | Out-Null
} catch {
    Write-Fail "Nao consegui conectar como '$($m.RootUser)'." `
               "Confira se o servico MySQL esta rodando (services.msc) e se a senha do root esta certa."
}
Write-Ok "conectado"

# ---------------------------------------------------------------------------
# O usuario e criado pra 'localhost' e '127.0.0.1'. O script oficial so cria
# 'localhost' e funciona porque o MySQL resolve 127.0.0.1 de volta pra localhost
# - mas se o servidor estiver com skip-name-resolve, isso quebra. Criar os dois
# evita esse "Access denied" chato.
# ---------------------------------------------------------------------------
$user = $m.User
$pass = $m.Password
$dbs  = @($m.AuthDb, $m.WorldDb, $m.CharDb)

$grantHosts = @('localhost', '127.0.0.1')

$sql = New-Object Text.StringBuilder
foreach ($h in $grantHosts) {
    [void]$sql.AppendLine("CREATE USER IF NOT EXISTS '$user'@'$h' IDENTIFIED BY '$pass';")
    [void]$sql.AppendLine("ALTER USER '$user'@'$h' IDENTIFIED BY '$pass';")
}
foreach ($db in $dbs) {
    [void]$sql.AppendLine("CREATE DATABASE IF NOT EXISTS ``$db`` DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_unicode_ci;")
    foreach ($h in $grantHosts) {
        [void]$sql.AppendLine("GRANT ALL PRIVILEGES ON ``$db``.* TO '$user'@'$h' WITH GRANT OPTION;")
    }
}
[void]$sql.AppendLine('FLUSH PRIVILEGES;')

Write-Step "Criando usuario '$user' e os bancos"
foreach ($db in $dbs) { Write-Info $db }

Invoke-MySql -Settings $settings -User $m.RootUser -Password $RootPassword -Sql $sql.ToString() | Out-Null
Write-Ok "usuario e bancos criados"

# --- validar que o usuario do servidor consegue mesmo entrar ---------------
Write-Step "Testando o login do usuario '$user'"
foreach ($db in $dbs) {
    Invoke-MySql -Settings $settings -User $user -Password $pass -Database $db -Sql 'SELECT 1;' | Out-Null
    Write-Ok "$db acessivel"
}

Write-Host @"

    Os bancos estao vazios de proposito - o worldserver popula sozinho no
    primeiro start.

    Proximo passo:
        .\scripts\05-extract-client-data.ps1
"@ -ForegroundColor Gray
