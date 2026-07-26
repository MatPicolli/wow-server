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

# Sem 'exit' aqui: sao DOIS problemas independentes, e o segundo - a senha
# errada dentro do playerbots.conf - continua existindo com o banco ja criado.
# Sair cedo neste ponto foi exatamente o que fez a primeira versao dizer
# "nada a corrigir" e deixar o servidor falhando igual.
if ($jaFunciona) {
    Write-Ok "'$($m.User)' ja acessa $db"
} else {
    Write-Info "'$($m.User)' ainda nao acessa $db"

    # ContainsKey e nao "-not $RootPassword": senha vazia e um valor legitimo, e
    # com a checagem ingenua ela cairia no Read-Host mesmo tendo sido informada.
    if (-not $PSBoundParameters.ContainsKey('RootPassword')) {
        $RootPassword = Read-MySqlRootPassword
    }

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
}

# --- a linha de conexao do proprio modulo ------------------------------------
# O playerbots.conf NAO le a senha do worldserver.conf: ele traz a propria
# string de conexao, com o padrao de fabrica "acore;acore". Quem trocou a senha
# do banco continua batendo em "access denied" mesmo com o banco ja criado e o
# grant correto - os tres pools principais funcionam, so o do Playerbots nao.
$confModulo = Join-Path (Join-Path (Join-Path $settings.ServerDir 'configs') 'modules') 'playerbots.conf'

Write-Step 'Conferindo a linha de conexao do playerbots.conf'

if (-not (Test-Path $confModulo)) {
    Write-Warn "nao achei $confModulo"
    Write-Info 'o modulo talvez ainda nao tenha sido implantado; rode .\scripts\rebuild.ps1'
} else {
    $esperado = "$($m.Host);$($m.Port);$($m.User);$($m.Password);$db"

    $texto = [IO.File]::ReadAllText($confModulo)

    # (?=\r?\n|$) e nao '$': em modo multiline o '$' casa antes do \n e deixa o
    # \r do CRLF de fora, e este arquivo e CRLF.
    $re = [regex]::new('(?m)^(?<lead>[ \t]*PlayerbotsDatabaseInfo[ \t]*=[ \t]*)(?<val>"[^"]*"|[^\r\n#]*?)(?<tail>[ \t]*(?:#[^\r\n]*)?)(?=\r?\n|$)')
    $achado = $re.Match($texto)

    if (-not $achado.Success) {
        Write-Warn 'nao achei a chave PlayerbotsDatabaseInfo neste arquivo'
        Write-Info "confira a mao: a linha deve ficar assim"
        Write-Info "  PlayerbotsDatabaseInfo = `"$esperado`""
    } else {
        $atual = $achado.Groups['val'].Value.Trim('"')

        if ($atual -eq $esperado) {
            Write-Ok 'a linha de conexao ja esta correta'
        } else {
            Write-Info "atual:    $atual"
            Write-Info "correto:  $esperado"

            $novo = $re.Replace($texto, {
                param($x)
                $x.Groups['lead'].Value + '"' + $esperado + '"' + $x.Groups['tail'].Value
            }, 1)

            $copia = "$confModulo.bak-" + (Get-Date -Format 'yyyy-MM-dd_HHmm')
            Copy-Item $confModulo $copia
            Write-TextFileNoBom -Path $confModulo -Content $novo

            Write-Ok 'linha de conexao corrigida'
            Write-Info "copia do original: $copia"
        }
    }
}

Write-Step 'Proximo passo'
Write-Info 'suba o servidor de novo: as tabelas do Playerbots sao criadas sozinhas'
Write-Info 'no primeiro start, igual aos outros bancos.'
exit 0
