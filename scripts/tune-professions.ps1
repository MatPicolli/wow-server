<#
.SYNOPSIS
    Ajusta quanto material vem de mineracao, herbalismo, pesca e esfolamento.

.DESCRIPTION
    Mexe no MinCount/MaxCount das tabelas de loot do banco acore_world. Um
    nodulo que dava 2-4 minerios passa a dar 4-8 com -Mining 2.

    Por padrao so MOSTRA o que faria. Use -Apply pra gravar.

    Os valores originais sao copiados pra tabela 'custom_gather_backup' na
    primeira execucao, e todo calculo parte dela. Sem isso, rodar "-Mining 2"
    duas vezes daria 4x sem voce perceber. Com ela, o resultado depende so do
    multiplicador que voce passou agora - e -Reset devolve tudo ao original.

.PARAMETER Mining
    Multiplicador pros minerios (item classe 7, subclasse 7 - metal e pedra).

.PARAMETER Herbalism
    Multiplicador pras ervas (item classe 7, subclasse 9).

.PARAMETER Fishing
    Multiplicador pra tudo que vem da pesca.

.PARAMETER Skinning
    Multiplicador pro esfolamento (couros).

.PARAMETER All
    Aplica o mesmo multiplicador nas quatro. Os individuais tem prioridade.

.PARAMETER Apply
    Grava de verdade. Sem isso, so preview.

.PARAMETER Reset
    Restaura os valores originais e esquece o backup.

.PARAMETER MaxStack
    Teto por seguranca (padrao 255). Nao adianta pedir 10000 minerios.

.EXAMPLE
    .\scripts\tune-professions.ps1 -Mining 3 -Herbalism 3
    Mostra o que aconteceria triplicando minerio e erva.

.EXAMPLE
    .\scripts\tune-professions.ps1 -All 2 -Apply
    Dobra tudo, pra valer.

.EXAMPLE
    .\scripts\tune-professions.ps1 -Reset -Apply
    Volta ao normal.

.NOTES
    Depois de aplicar, recarregue sem reiniciar - no console do worldserver:
        reload gameobject_loot_template
        reload fishing_loot_template
        reload skinning_loot_template
#>
[CmdletBinding()]
param(
    [double]$Mining    = 0,
    [double]$Herbalism = 0,
    [double]$Fishing   = 0,
    [double]$Skinning  = 0,
    [double]$All       = 0,

    [switch]$Apply,
    [switch]$Reset,
    [int]$MaxStack = 255,
    [string]$OutFile
)

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings
$m        = $settings.MySql
$backup   = 'custom_gather_backup'

# ---------------------------------------------------------------------------
# Mineracao e herbalismo saem os dois de gameobject_loot_template (os nodulos
# sao gameobjects do tipo bau). Separamos um do outro pela classe/subclasse do
# item: 7/7 = metal e pedra, 7/9 = erva.
# ---------------------------------------------------------------------------
$professions = @(
    [pscustomobject]@{
        Name = 'Mining';    Table = 'gameobject_loot_template'
        Join = 'JOIN item_template it ON it.entry = t.Item'
        Where = 'it.class = 7 AND it.subclass = 7'
        Rate = $(if ($Mining    -gt 0) { $Mining }    else { $All })
    }
    [pscustomobject]@{
        Name = 'Herbalism'; Table = 'gameobject_loot_template'
        Join = 'JOIN item_template it ON it.entry = t.Item'
        Where = 'it.class = 7 AND it.subclass = 9'
        Rate = $(if ($Herbalism -gt 0) { $Herbalism } else { $All })
    }
    [pscustomobject]@{
        Name = 'Fishing';   Table = 'fishing_loot_template'
        Join = ''; Where = '1 = 1'
        Rate = $(if ($Fishing   -gt 0) { $Fishing }   else { $All })
    }
    [pscustomobject]@{
        Name = 'Skinning';  Table = 'skinning_loot_template'
        Join = ''; Where = '1 = 1'
        Rate = $(if ($Skinning  -gt 0) { $Skinning }  else { $All })
    }
)

$selected = $professions | Where-Object { $_.Rate -gt 0 }

if (-not $Reset -and -not $selected) {
    Write-Host @"

    Nenhum multiplicador informado.

    Exemplos:
        .\scripts\tune-professions.ps1 -Mining 3 -Herbalism 3
        .\scripts\tune-professions.ps1 -All 2 -Apply
        .\scripts\tune-professions.ps1 -Reset -Apply

    Rode 'Get-Help .\scripts\tune-professions.ps1 -Detailed' pra ver tudo.
"@ -ForegroundColor Yellow
    exit 0
}

function Invoke-World {
    param([string]$Sql)
    return Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                        -Database $m.WorldDb -Sql $Sql
}

Write-Step "Conectando em $($m.WorldDb)"
try {
    Invoke-World -Sql 'SELECT 1;' | Out-Null
} catch {
    Write-Fail "Nao consegui conectar no banco de mundo." `
               "Confira se o MySQL esta rodando e se o 04-setup-database.ps1 ja rodou."
}
Write-Ok 'conectado'

# --- garante a tabela de backup -------------------------------------------
Invoke-World -Sql @"
CREATE TABLE IF NOT EXISTS ``$backup`` (
    LootTable VARCHAR(64)  NOT NULL,
    Entry     INT UNSIGNED NOT NULL,
    Item      INT UNSIGNED NOT NULL,
    MinCount  INT          NOT NULL,
    MaxCount  INT          NOT NULL,
    PRIMARY KEY (LootTable, Entry, Item)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
"@ | Out-Null

# --- reset -----------------------------------------------------------------
if ($Reset) {
    Write-Step "Restaurando os valores originais"

    $rows = (Invoke-World -Sql "SELECT COUNT(*) FROM ``$backup``;") -split "`r?`n" |
            Select-Object -Last 1
    Write-Info "$($rows.Trim()) linhas guardadas no backup"

    if (-not $Apply) {
        Write-Warn "preview - nada foi alterado. Repita com -Apply pra restaurar."
        exit 0
    }

    foreach ($table in @('gameobject_loot_template', 'fishing_loot_template', 'skinning_loot_template')) {
        Invoke-World -Sql @"
UPDATE ``$table`` t
  JOIN ``$backup`` b ON b.LootTable = '$table' AND b.Entry = t.Entry AND b.Item = t.Item
   SET t.MinCount = b.MinCount,
       t.MaxCount = b.MaxCount;
"@ | Out-Null
        Write-Ok $table
    }

    Invoke-World -Sql "DROP TABLE ``$backup``;" | Out-Null
    Write-Ok 'backup descartado - tudo voltou ao original'
    Write-Info 'no console do worldserver: reload gameobject_loot_template'
    exit 0
}

# --- backup dos originais --------------------------------------------------
Write-Step "Guardando os valores originais"
foreach ($p in $selected) {
    $join = $p.Join
    Invoke-World -Sql @"
INSERT IGNORE INTO ``$backup`` (LootTable, Entry, Item, MinCount, MaxCount)
SELECT '$($p.Table)', t.Entry, t.Item, t.MinCount, t.MaxCount
  FROM ``$($p.Table)`` t
  $join
 WHERE $($p.Where);
"@ | Out-Null
}
Write-Ok 'originais preservados (INSERT IGNORE - nao sobrescreve backup anterior)'

# --- preview ---------------------------------------------------------------
$sqlScript = New-Object Text.StringBuilder
[void]$sqlScript.AppendLine("-- Gerado por tune-professions.ps1 em $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
[void]$sqlScript.AppendLine("-- Reaplicavel: os valores saem sempre de $backup, nunca do valor atual.")
[void]$sqlScript.AppendLine()

foreach ($p in $selected) {
    Write-Step "$($p.Name)  x$($p.Rate)"

    $join = $p.Join
    $preview = Invoke-World -Sql @"
SELECT COUNT(*) AS Linhas,
       MIN(b.MinCount) AS MinAtual, MAX(b.MaxCount) AS MaxAtual,
       LEAST($MaxStack, GREATEST(1, ROUND(MIN(b.MinCount) * $($p.Rate)))) AS MinNovo,
       LEAST($MaxStack, GREATEST(1, ROUND(MAX(b.MaxCount) * $($p.Rate)))) AS MaxNovo
  FROM ``$($p.Table)`` t
  JOIN ``$backup`` b ON b.LootTable = '$($p.Table)' AND b.Entry = t.Entry AND b.Item = t.Item
  $join
 WHERE $($p.Where);
"@
    Write-Host $preview -ForegroundColor Gray

    $exemplos = Invoke-World -Sql @"
SELECT it2.name AS Item, b.MinCount AS DeMin, b.MaxCount AS DeMax,
       LEAST($MaxStack, GREATEST(1, ROUND(b.MinCount * $($p.Rate)))) AS ParaMin,
       LEAST($MaxStack, GREATEST(1, ROUND(b.MaxCount * $($p.Rate)))) AS ParaMax
  FROM ``$($p.Table)`` t
  JOIN ``$backup`` b ON b.LootTable = '$($p.Table)' AND b.Entry = t.Entry AND b.Item = t.Item
  $join
  JOIN item_template it2 ON it2.entry = t.Item
 WHERE $($p.Where)
 GROUP BY t.Item
 LIMIT 5;
"@
    Write-Info 'exemplos:'
    Write-Host $exemplos -ForegroundColor DarkGray

    $update = @"
UPDATE ``$($p.Table)`` t
  JOIN ``$backup`` b ON b.LootTable = '$($p.Table)' AND b.Entry = t.Entry AND b.Item = t.Item
  $join
   SET t.MinCount = LEAST($MaxStack, GREATEST(1, ROUND(b.MinCount * $($p.Rate)))),
       t.MaxCount = LEAST($MaxStack, GREATEST(1, ROUND(b.MaxCount * $($p.Rate))))
 WHERE $($p.Where);
"@
    [void]$sqlScript.AppendLine("-- $($p.Name) x$($p.Rate)")
    [void]$sqlScript.AppendLine($update)
    [void]$sqlScript.AppendLine()

    if ($Apply) {
        Invoke-World -Sql $update | Out-Null
        Write-Ok "$($p.Name) aplicado"
    }
}

# --- salvar o SQL ----------------------------------------------------------
# data/sql/custom/db_world e aplicado pelo auto-updater do worldserver, entao
# a customizacao sobrevive a recriar o banco do zero.
if (-not $OutFile) {
    $OutFile = Join-Path $settings.SourceDir 'data\sql\custom\db_world\tune_professions.sql'
}
New-DirectoryIfMissing (Split-Path -Parent $OutFile)
Write-TextFileNoBom -Path $OutFile -Content $sqlScript.ToString()
Write-Step "SQL salvo"
Write-Ok $OutFile
Write-Info 'fica no custom/db_world, entao e reaplicado se voce recriar o banco'

if (-not $Apply) {
    Write-Host @"

    PREVIEW - nada foi alterado no banco.
    Repita o comando com -Apply pra valer.
"@ -ForegroundColor Yellow
} else {
    Write-Host @"

    Aplicado. Pra o servidor enxergar sem reiniciar, no console do worldserver:

        reload gameobject_loot_template
        reload fishing_loot_template
        reload skinning_loot_template

    Pra voltar tudo ao original:
        .\scripts\tune-professions.ps1 -Reset -Apply
"@ -ForegroundColor Green
}
