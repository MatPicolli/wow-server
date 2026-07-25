<#
.SYNOPSIS
    Ajusta a CHANCE de drop (nao a quantidade).

.DESCRIPTION
    O tune-professions.ps1 mexe em quanto vem quando cai. Este mexe em quanto
    e frequente cair - a coluna Chance das tabelas de loot, em porcentagem.

    O caso principal e item de quest: aquele "mate 10 lobos e colete 8 presas"
    em que so 3 lobos dropam. Com -QuestItems 3 a chance triplica, limitada a
    100%.

    Mesma protecao do outro script: os valores originais vao pra tabela
    custom_drop_backup e todo calculo parte dela, entao rodar duas vezes nao
    acumula e -Reset devolve exatamente o original.

.PARAMETER QuestItems
    Multiplicador da chance de itens de quest (QuestRequired = 1).

.PARAMETER CreatureItems
    Multiplicador da chance de TODO loot de criatura que nao e de quest.
    Cuidado: afeta o jogo inteiro.

.PARAMETER Apply
    Grava. Sem isso, so preview.

.PARAMETER Reset
    Restaura os valores originais e descarta o backup.

.EXAMPLE
    .\scripts\tune-drop-chance.ps1 -QuestItems 3
    Preview: triplica a chance de item de quest.

.EXAMPLE
    .\scripts\tune-drop-chance.ps1 -QuestItems 3 -Apply
#>
[CmdletBinding()]
param(
    [double]$QuestItems    = 0,
    [double]$CreatureItems = 0,
    [switch]$Apply,
    [switch]$Reset,
    [string]$OutFile
)

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings
$m        = $settings.MySql
$backup   = 'custom_drop_backup'

$alvos = @(
    [pscustomobject]@{
        Name  = 'Itens de quest'
        Table = 'creature_loot_template'
        Where = 'QuestRequired = 1'
        Rate  = $QuestItems
    }
    [pscustomobject]@{
        Name  = 'Loot comum de criatura'
        Table = 'creature_loot_template'
        Where = 'QuestRequired = 0'
        Rate  = $CreatureItems
    }
)
$selecionados = $alvos | Where-Object { $_.Rate -gt 0 }

if (-not $Reset -and -not $selecionados) {
    Write-Host @"

    Nenhum multiplicador informado.

    Exemplos:
        .\scripts\tune-drop-chance.ps1 -QuestItems 3
        .\scripts\tune-drop-chance.ps1 -QuestItems 3 -Apply
        .\scripts\tune-drop-chance.ps1 -Reset -Apply
"@ -ForegroundColor Yellow
    exit 0
}

function Invoke-World {
    param([string]$Sql)
    return Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                        -Database $m.WorldDb -Sql $Sql
}

Write-Step "Conectando em $($m.WorldDb)"
try { Invoke-World -Sql 'SELECT 1;' | Out-Null }
catch { Write-Fail "Nao consegui conectar no banco de mundo." "Confira se o MySQL esta rodando." }
Write-Ok 'conectado'

# A chave inclui QuestRequired: o mesmo item pode aparecer como loot de quest
# num mob e loot comum em outro, e cada caso tem multiplicador proprio.
Invoke-World -Sql @"
CREATE TABLE IF NOT EXISTS ``$backup`` (
    LootTable     VARCHAR(64)  NOT NULL,
    Entry         INT UNSIGNED NOT NULL,
    Item          INT UNSIGNED NOT NULL,
    QuestRequired TINYINT      NOT NULL,
    Chance        FLOAT        NOT NULL,
    PRIMARY KEY (LootTable, Entry, Item)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
"@ | Out-Null

# --- reset -----------------------------------------------------------------
if ($Reset) {
    Write-Step 'Restaurando as chances originais'
    if (-not $Apply) {
        Write-Warn 'preview - nada alterado. Repita com -Apply.'
        exit 0
    }
    foreach ($t in ($alvos.Table | Sort-Object -Unique)) {
        Invoke-World -Sql @"
UPDATE ``$t`` t
  JOIN ``$backup`` b ON b.LootTable = '$t' AND b.Entry = t.Entry AND b.Item = t.Item
   SET t.Chance = b.Chance;
"@ | Out-Null
        Write-Ok $t
    }
    Invoke-World -Sql "DROP TABLE ``$backup``;" | Out-Null
    Write-Ok 'tudo de volta ao original'
    Write-Info 'no console do worldserver: reload creature_loot_template'
    exit 0
}

# --- backup ----------------------------------------------------------------
Write-Step 'Guardando as chances originais'
foreach ($a in $selecionados) {
    Invoke-World -Sql @"
INSERT IGNORE INTO ``$backup`` (LootTable, Entry, Item, QuestRequired, Chance)
SELECT '$($a.Table)', Entry, Item, QuestRequired, Chance
  FROM ``$($a.Table)``
 WHERE $($a.Where) AND Chance > 0;
"@ | Out-Null
}
Write-Ok 'originais preservados'

# --- aplicar ---------------------------------------------------------------
$sqlScript = New-Object Text.StringBuilder
[void]$sqlScript.AppendLine("-- Gerado por tune-drop-chance.ps1 em $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
[void]$sqlScript.AppendLine()

foreach ($a in $selecionados) {
    Write-Step "$($a.Name)  x$($a.Rate)"

    $preview = Invoke-World -Sql @"
SELECT COUNT(*) AS Linhas,
       ROUND(MIN(b.Chance), 2) AS MenorAtual,
       ROUND(MAX(b.Chance), 2) AS MaiorAtual,
       ROUND(LEAST(100, MIN(b.Chance) * $($a.Rate)), 2) AS MenorNova,
       ROUND(LEAST(100, MAX(b.Chance) * $($a.Rate)), 2) AS MaiorNova,
       SUM(CASE WHEN b.Chance * $($a.Rate) >= 100 THEN 1 ELSE 0 END) AS ViramCerteza
  FROM ``$($a.Table)`` t
  JOIN ``$backup`` b ON b.LootTable = '$($a.Table)' AND b.Entry = t.Entry AND b.Item = t.Item
 WHERE t.$($a.Where) AND b.QuestRequired = $(if ($a.Where -match 'QuestRequired = 1') { 1 } else { 0 });
"@
    Write-Host $preview -ForegroundColor Gray

    # Chance e porcentagem: passar de 100 nao faz sentido, e 100 significa
    # "dropa sempre".
    $update = @"
UPDATE ``$($a.Table)`` t
  JOIN ``$backup`` b ON b.LootTable = '$($a.Table)' AND b.Entry = t.Entry AND b.Item = t.Item
   SET t.Chance = LEAST(100, b.Chance * $($a.Rate))
 WHERE t.$($a.Where);
"@
    [void]$sqlScript.AppendLine("-- $($a.Name) x$($a.Rate)")
    [void]$sqlScript.AppendLine($update)
    [void]$sqlScript.AppendLine()

    if ($Apply) {
        Invoke-World -Sql $update | Out-Null
        Write-Ok "$($a.Name) aplicado"
    }
}

if (-not $OutFile) {
    $OutFile = Join-Path $settings.SourceDir 'data\sql\custom\db_world\tune_drop_chance.sql'
}
New-DirectoryIfMissing (Split-Path -Parent $OutFile)
Write-TextFileNoBom -Path $OutFile -Content $sqlScript.ToString()
Write-Ok "SQL salvo em $OutFile"

if (-not $Apply) {
    Write-Host "`n    PREVIEW - nada alterado. Repita com -Apply.`n" -ForegroundColor Yellow
} else {
    Write-Host @"

    Aplicado. No console do worldserver:
        reload creature_loot_template

    Pra voltar:
        .\scripts\tune-drop-chance.ps1 -Reset -Apply
"@ -ForegroundColor Green
}
