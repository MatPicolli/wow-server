<#
.SYNOPSIS
    Aplica um arquivo .sql num dos bancos do servidor, com backup antes.

.DESCRIPTION
    Serve para o SQL que a GUI gera - a aba "Criar itens" e quem usa hoje.
    Mostra o arquivo e nao grava nada sem -Apply.

    As protecoes existem porque este e o unico script do repositorio que
    escreve SQL vindo de fora:

      - o arquivo tem que existir e nao pode estar vazio;
      - o banco de destino tem que ser um dos que estao no settings.psd1 -
        assim um caminho errado nao escreve num banco que nao e nosso;
      - faz backup do banco antes de aplicar e ABORTA se o backup falhar;
      - roda tudo numa transacao unica, entao um erro no meio nao deixa
        metade aplicada.

.PARAMETER File
    Caminho do .sql.

.PARAMETER Database
    Qual banco. Padrao: o WorldDb do settings.psd1.

.PARAMETER Apply
    Grava de verdade. Sem isso so mostra.

.PARAMETER SkipBackup
    Pula o backup. So para quem sabe o que esta fazendo.

.EXAMPLE
    .\scripts\apply-sql.ps1 -File .\meu-item.sql
    .\scripts\apply-sql.ps1 -File .\meu-item.sql -Apply
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$File,
    [string]$Database,
    [switch]$Apply,
    [switch]$SkipBackup
)

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings
$m = $settings.MySql

if (-not $Database) { $Database = $m.WorldDb }

# So os bancos deste servidor. Sem isto, um -Database digitado errado poderia
# escrever em qualquer schema a que o usuario tenha acesso.
$permitidos = @($m.WorldDb, $m.CharDb, $m.AuthDb, $m.PlayerbotsDb)
if ($permitidos -notcontains $Database) {
    Write-Fail "banco '$Database' nao e um dos bancos deste servidor." `
               ("Permitidos: " + ($permitidos -join ', '))
}

if (-not (Test-Path $File)) {
    Write-Fail "arquivo nao encontrado: $File"
}

$conteudo = Get-Content -Raw $File -ErrorAction Stop
if (-not $conteudo -or -not $conteudo.Trim()) {
    Write-Fail "o arquivo esta vazio: $File"
}

Write-Step "Arquivo"
Write-Info $File
Write-Info ("{0} linha(s), {1} caracteres" -f ($conteudo -split "`r?`n").Count, $conteudo.Length)

Write-Step "Banco de destino"
Write-Info $Database

Write-Step 'Conteudo'
foreach ($linha in ($conteudo -split "`r?`n")) {
    if ($linha.Trim()) { Write-Host "    $linha" -ForegroundColor DarkGray }
}

if (-not $Apply) {
    Write-Host ''
    Write-Info 'nada foi gravado. Para aplicar de verdade:'
    Write-Info "  .\scripts\apply-sql.ps1 -File `"$File`" -Apply"
    exit 0
}

if (-not (Test-MySqlReachable -Settings $settings)) {
    Write-Fail "o MySQL nao esta respondendo em $($m.Host):$($m.Port)." `
               'Rode .\scripts\start-mysql.ps1 (como Administrador).'
}

# --- backup ------------------------------------------------------------------
if (-not $SkipBackup) {
    Write-Step 'Backup antes de gravar'

    $dump = Get-MySqlDumpExe
    if (-not $dump) {
        Write-Fail 'mysqldump nao encontrado.' `
                   'Sem backup nao gravo. Use -SkipBackup se aceitar o risco.'
    }

    $pasta = Join-Path $settings.Root 'backups'
    New-DirectoryIfMissing $pasta
    $saida = Join-Path $pasta ("{0}-antes-de-sql-{1}.sql" -f $Database, (Get-Date -Format 'yyyy-MM-dd_HHmm'))

    $dumpArgs = @(
        "--host=$($m.Host)"
        "--port=$($m.Port)"
        "--user=$($m.User)"
        '--protocol=TCP'
        '--single-transaction'
        # MySQL 8 exige PROCESS para ler INFORMATION_SCHEMA.FILES, e o usuario
        # do servidor nao tem esse privilegio.
        '--no-tablespaces'
        "--result-file=$saida"
        $Database
    )

    $prevPwd = $env:MYSQL_PWD
    if ($m.Password) { $env:MYSQL_PWD = $m.Password }

    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $codigo = -1
    try {
        & $dump @dumpArgs 2>"$saida.err"
        $codigo = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prevEap
        $env:MYSQL_PWD = $prevPwd
    }

    $erroDump = ''
    if (Test-Path "$saida.err") {
        $erroDump = (Get-Content -Raw "$saida.err" -ErrorAction SilentlyContinue)
        Remove-Item "$saida.err" -Force -ErrorAction SilentlyContinue
    }

    if ($codigo -ne 0) {
        if ($erroDump) { Write-Host $erroDump -ForegroundColor DarkGray }
        Remove-Item $saida -Force -ErrorAction SilentlyContinue
        Write-Fail "o backup falhou (codigo $codigo) - nao vou gravar nada." `
                   'Resolva o backup primeiro, ou use -SkipBackup se aceitar o risco.'
    }

    $tamanho = (Get-Item $saida).Length
    if ($tamanho -lt 1024) {
        Remove-Item $saida -Force -ErrorAction SilentlyContinue
        Write-Fail "o backup saiu com $tamanho bytes - pequeno demais para ser real." `
                   'Nao vou gravar nada.'
    }

    Write-Ok ("backup: {0} ({1:N1} MB)" -f (Split-Path -Leaf $saida), ($tamanho / 1MB))
}

# --- aplicar -----------------------------------------------------------------
Write-Step 'Aplicando'

# Tudo numa transacao: um erro no meio desfaz o que ja tinha passado, em vez de
# deixar o item pela metade no banco.
$envolvido = "START TRANSACTION;`n" + $conteudo.TrimEnd() + "`nCOMMIT;`n"

$temp = Join-Path ([IO.Path]::GetTempPath()) ("apply-" + [guid]::NewGuid().ToString('N') + '.sql')

# Write-TextFileNoBom e obrigatorio: o Set-Content -Encoding UTF8 do PowerShell
# 5.1 escreve BOM, e o cliente do MySQL trata o BOM como parte do primeiro
# comando - o erro que sai fala de sintaxe perto de um caractere invisivel.
Write-TextFileNoBom -Path $temp -Content $envolvido

try {
    Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                 -Database $Database -File $temp | Out-Null
    Write-Ok 'aplicado'
} finally {
    Remove-Item $temp -Force -ErrorAction SilentlyContinue
}

Write-Step 'Proximo passo'
Write-Info 'para o servidor enxergar item novo sem reiniciar, rode no console:'
Write-Info '  reload item_template'

exit 0
