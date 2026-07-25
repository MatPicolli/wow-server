<#
.SYNOPSIS
    Faz backup dos bancos do servidor.

.DESCRIPTION
    Por padrao salva acore_characters e acore_auth - seus personagens e suas
    contas. Sao os unicos dados insubstituiveis: acore_world e conteudo do
    jogo, recriado pelo auto-updater a qualquer momento.

    Pode rodar com o servidor ligado. O --single-transaction tira um retrato
    consistente sem travar as tabelas.

.PARAMETER IncludeWorld
    Inclui tambem o acore_world. Vale a pena se voce ja customizou itens,
    NPCs ou loot direto no banco.

.PARAMETER Path
    Pasta de destino. Padrao: <Root>\backups.

.PARAMETER KeepLast
    Quantos backups manter. Os mais antigos sao apagados. 0 = nao apaga nada.

.EXAMPLE
    .\scripts\backup-db.ps1

.EXAMPLE
    .\scripts\backup-db.ps1 -IncludeWorld -KeepLast 10
#>
[CmdletBinding()]
param(
    [switch]$IncludeWorld,
    [string]$Path,
    [int]$KeepLast = 10
)

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings
$m        = $settings.MySql

$dump = Get-MySqlDumpExe
if (-not $dump) {
    Write-Fail "mysqldump nao encontrado." `
               "Adicione a pasta bin\ do MySQL ao PATH (o 01-install-prereqs.ps1 faz isso)."
}

if (-not $Path) { $Path = Join-Path $settings.Root 'backups' }
New-DirectoryIfMissing $Path

$bancos = @($m.CharDb, $m.AuthDb)
if ($IncludeWorld) { $bancos += $m.WorldDb }

$stamp = Get-Date -Format 'yyyy-MM-dd_HHmm'
$saida = Join-Path $Path "wow-$stamp.sql"

Write-Step "Salvando $($bancos -join ', ')"
Write-Info "destino: $saida"

$dumpArgs = @(
    "--host=$($m.Host)"
    "--port=$($m.Port)"
    "--user=$($m.User)"
    "--password=$($m.Password)"
    '--protocol=TCP'
    '--single-transaction'      # retrato consistente sem travar o servidor
    '--routines'
    '--events'
    # --result-file faz o proprio mysqldump escrever o arquivo. Redirecionar
    # pelo PowerShell passaria por Set-Content/Out-File, que no Windows
    # PowerShell 5.1 gravam UTF-8 COM BOM - e um BOM no inicio quebra o
    # 'mysql < arquivo.sql' na hora de restaurar.
    "--result-file=$saida"
    '--databases'
) + $bancos

# Mesmo cuidado do Invoke-MySql: com $ErrorActionPreference = 'Stop', o stderr
# de um comando nativo vira excecao antes de conseguirmos ler o exit code, e a
# mensagem real se perde.
$prevEap = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$code = -1
try {
    & $dump @dumpArgs 2>"$saida.err"
    $code = $LASTEXITCODE
} finally {
    $ErrorActionPreference = $prevEap
}

$erros = ''
if (Test-Path "$saida.err") {
    $erros = (Get-Content -Raw "$saida.err" -ErrorAction SilentlyContinue)
    Remove-Item "$saida.err" -Force -ErrorAction SilentlyContinue
}

if ($code -ne 0) {
    if ($erros) { Write-Host $erros -ForegroundColor DarkGray }
    Remove-Item $saida -Force -ErrorAction SilentlyContinue
    Write-Fail "mysqldump falhou (exit code $code)." "Confira se o MySQL esta rodando e se a senha do usuario '$($m.User)' esta certa."
}

# Um arquivo vazio ou minusculo significa que nao veio nada - melhor gritar
# agora do que voce descobrir na hora de restaurar.
$tamanho = (Get-Item $saida).Length
if ($tamanho -lt 1KB) {
    Remove-Item $saida -Force -ErrorAction SilentlyContinue
    Write-Fail "O backup saiu vazio ($tamanho bytes)." "Alguma coisa deu errado - confira o acesso ao banco."
}

Write-Ok ("{0}  ({1:N1} MB)" -f (Split-Path $saida -Leaf), ($tamanho / 1MB))

# --- limpeza dos antigos ---------------------------------------------------
if ($KeepLast -gt 0) {
    $antigos = Get-ChildItem $Path -Filter 'wow-*.sql' -File |
               Sort-Object LastWriteTime -Descending |
               Select-Object -Skip $KeepLast
    foreach ($f in $antigos) {
        Remove-Item $f.FullName -Force
        Write-Info "removido antigo: $($f.Name)"
    }
}

$total = (Get-ChildItem $Path -Filter 'wow-*.sql' -File | Measure-Object).Count
Write-Ok "$total backup(s) em $Path"

Write-Host @"

    Pra restaurar:
        mysql -u root -p < "$saida"

    Restaurar sobrescreve os bancos que estao no arquivo. Derrube o servidor
    antes (.\scripts\stop-server.ps1).
"@ -ForegroundColor Gray
