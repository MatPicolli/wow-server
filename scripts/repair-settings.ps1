<#
.SYNOPSIS
    Conserta um config\settings.psd1 com chaves duplicadas.

.DESCRIPTION
    Uma versao com bug da GUI podia acrescentar chaves em vez de substituir,
    deixando o arquivo com entradas repetidas. O Import-PowerShellDataFile
    recusa hashtable com chave repetida, entao TODOS os scripts param de
    funcionar com a mensagem "nao pode ser analisado como um arquivo de dados
    do PowerShell".

    Este script remove as repeticoes mantendo a ULTIMA ocorrencia de cada
    chave - que e justamente o valor mais recente, gravado pela GUI. O arquivo
    original e preservado com a extensao .bak.

.PARAMETER Path
    Caminho do settings.psd1. Padrao: config\settings.psd1 do repositorio.

.PARAMETER WhatIf
    Mostra o que faria, sem gravar.
#>
[CmdletBinding()]
param(
    [string]$Path,
    [switch]$WhatIf
)

. "$PSScriptRoot\lib\common.ps1"

if (-not $Path) { $Path = Join-Path $AcRepoDir 'config\settings.psd1' }

if (-not (Test-Path $Path)) {
    Write-Fail "'$Path' nao existe." "Copie config\settings.example.psd1 para config\settings.psd1."
}

Write-Step "Analisando $Path"

$linhas = Get-Content -LiteralPath $Path
$nivel  = 0
$mapa   = @{}   # "nivel|chave" -> lista de indices de linha

for ($i = 0; $i -lt $linhas.Count; $i++) {
    $linha = $linhas[$i]
    $semComentario = if ($linha.TrimStart().StartsWith('#')) { '' } else { $linha }

    if ($semComentario -match '^\s*([A-Za-z_]\w*)\s*=') {
        $chave = "$nivel|$($Matches[1])"
        if (-not $mapa.ContainsKey($chave)) { $mapa[$chave] = @() }
        $mapa[$chave] += $i
    }

    # profundidade depois de contar a chave: 'MySql = @{' declara MySql no
    # nivel de fora e so entao abre um nivel novo
    $nivel += ([regex]::Matches($semComentario, '\{')).Count
    $nivel -= ([regex]::Matches($semComentario, '\}')).Count
}

$repetidas = $mapa.GetEnumerator() | Where-Object { $_.Value.Count -gt 1 }

if (-not $repetidas) {
    Write-Ok 'nenhuma chave repetida - o arquivo esta integro'
    Write-Info 'Se os scripts ainda reclamam, o problema e outro. Rode:'
    Write-Info "  Import-PowerShellDataFile -LiteralPath `"$Path`""
    exit 0
}

# Descarta todas as ocorrencias menos a ultima de cada chave
$descartar = [System.Collections.Generic.HashSet[int]]::new()
foreach ($e in $repetidas) {
    $nome = ($e.Key -split '\|')[1]
    $todas = $e.Value
    Write-Warn "$nome aparece $($todas.Count)x - mantendo a ultima (linha $($todas[-1] + 1))"
    foreach ($idx in $todas[0..($todas.Count - 2)]) { [void]$descartar.Add($idx) }
}

$novo = @()
for ($i = 0; $i -lt $linhas.Count; $i++) {
    if (-not $descartar.Contains($i)) { $novo += $linhas[$i] }
}

if ($WhatIf) {
    Write-Step 'Previa (nada foi gravado)'
    $novo | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
    exit 0
}

$backup = "$Path.bak"
Copy-Item -LiteralPath $Path -Destination $backup -Force
Write-Ok "original preservado em $backup"

Write-TextFileNoBom -Path $Path -Content (($novo -join "`r`n") + "`r`n")
Write-Ok "$($descartar.Count) linha(s) duplicada(s) removida(s)"

Write-Step 'Conferindo se o PowerShell consegue ler'
try {
    $s = Import-PowerShellDataFile -LiteralPath $Path
    Write-Ok 'arquivo valido'
    Write-Info "ClientDir = $($s.ClientDir)"
    Write-Info "MySql.User = $($s.MySql.User)"
} catch {
    Write-Fail "Ainda nao da pra ler: $($_.Exception.Message)" `
               "Restaure o backup ('$backup') e recomece de config\settings.example.psd1."
}
