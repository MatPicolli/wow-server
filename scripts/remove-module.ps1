<#
.SYNOPSIS
    Remove um modulo instalado.

.DESCRIPTION
    Apaga a pasta do modulo em modules/. O que ele ja escreveu no banco NAO e
    desfeito - o updater do worldserver aplica SQL, nao reverte. Na pratica isso
    raramente incomoda: tabelas e colunas extras ficam sem uso.

    Preview por padrao. Sem -Apply nada e apagado.

.PARAMETER Name
    Nome da pasta em modules/, ex.: mod-eluna.

.PARAMETER Apply
    Apaga de verdade.

.PARAMETER Force
    Apaga mesmo que a pasta tenha alteracoes locais nao commitadas.

.EXAMPLE
    .\scripts\remove-module.ps1 -Name mod-eluna
    .\scripts\remove-module.ps1 -Name mod-eluna -Apply
    .\scripts\rebuild.ps1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Name,
    [switch]$Apply,
    [switch]$Force
)

. "$PSScriptRoot\lib\common.ps1"

$settings   = Import-ServerSettings
$modulesDir = Join-Path $settings.SourceDir 'modules'

# --- validar o nome ----------------------------------------------------------
# O nome vira caminho. Uma barra ou um '..' aqui apagaria algo fora de modules/,
# e este script apaga recursivamente e sem confirmacao adicional.
$limpo = $Name.Trim()
if ($limpo -notmatch '^[A-Za-z0-9._-]+$' -or $limpo -like '.*') {
    Write-Fail "nome de modulo invalido: '$Name'." 'Use so o nome da pasta, ex.: mod-eluna'
}

$alvo = Join-Path $modulesDir $limpo

# Cinto de seguranca: mesmo com o nome validado, conferir que o caminho
# resolvido continua dentro de modules/.
$raizCheia = [IO.Path]::GetFullPath($modulesDir)
$alvoCheio = [IO.Path]::GetFullPath($alvo)
if (-not $alvoCheio.StartsWith($raizCheia, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Fail "'$limpo' resolveria para fora de $modulesDir." 'Nada foi apagado.'
}

Write-Step "Remover o modulo $limpo"

if (-not (Test-Path $alvo)) {
    Write-Warn "$limpo nao esta instalado em $modulesDir"
    exit 0
}

# --- o que ha ali ------------------------------------------------------------
$arquivos = 0
$bytes = 0
try {
    Get-ChildItem $alvo -Recurse -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
        $arquivos++
        $bytes += $_.Length
    }
} catch { }

Write-Info "pasta:    $alvo"
Write-Info ("conteudo: {0} arquivos, {1:N1} MB" -f $arquivos, ($bytes / 1MB))

$origem = ''
if (Test-Path (Join-Path $alvo '.git')) {
    try { $origem = (git -C $alvo remote get-url origin 2>$null | Out-String).Trim() } catch { }
    if ($origem) { Write-Info "origem:   $origem  (da para reinstalar depois)" }

    # Trabalho local nao commitado nao volta de lugar nenhum.
    $sujo = ''
    try { $sujo = (git -C $alvo status --porcelain 2>$null | Out-String).Trim() } catch { }
    if ($sujo) {
        Write-Warn 'ha alteracoes locais nao commitadas nesta pasta:'
        foreach ($linha in ($sujo -split "`r?`n" | Select-Object -First 10)) { Write-Info "  $linha" }
        if (-not $Force) {
            Write-Fail 'o modulo tem alteracoes locais que seriam perdidas.' `
                       'Confira, e repita com -Force se quiser apagar mesmo assim.'
        }
        Write-Warn '-Force: apagando junto com as alteracoes locais'
    }
} else {
    Write-Warn 'esta pasta nao e um clone git - nao havera como reinstalar automaticamente'
}

Write-Info 'o que este script NAO faz: desfazer o SQL que o modulo ja aplicou no banco'

if (-not $Apply) {
    Write-Step 'Isto foi so uma previa'
    Write-Info 'para apagar de verdade, repita o comando com  -Apply'
    exit 0
}

# --- apagar ------------------------------------------------------------------
# Objetos do git vem somente-leitura, e Remove-Item para no primeiro deles.
Write-Step 'Apagando'
try {
    Get-ChildItem $alvo -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
        try { $_.Attributes = [IO.FileAttributes]::Normal } catch { }
    }
    Remove-Item $alvo -Recurse -Force
} catch {
    Write-Fail "nao consegui apagar $alvo`: $_" `
               'Feche programas que estejam usando a pasta (editor, explorador de arquivos) e tente de novo.'
}

if (Test-Path $alvo) { Write-Fail "a pasta $alvo continua la." }

Write-Ok "$limpo removido"
Write-Step 'Proximo passo'
Write-Info 'rode .\scripts\rebuild.ps1 para o servidor deixar de incluir o modulo'
exit 0
