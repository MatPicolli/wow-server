<#
.SYNOPSIS
    Conserta as duas coisas que impedem um servidor recem-instalado de subir.

.DESCRIPTION
    Dois problemas diferentes, os dois em instalacao nova, os dois com
    mensagem que nao aponta para a causa:

    1) O worldserver morre logo no inicio, carregando os DBC:

        [1146] Table 'acore_world.charsections_dbc' doesn't exist
        Your database structure is not up to date. Please make sure you've
        executed all queries in the sql/updates folders.

       A mensagem manda rodar os updates, e os updates ja rodaram - o proprio
       log diz "World database is up-to-date!" na linha de cima. O codigo
       compilado consulta duas tabelas que nao existem no SQL do fork:
       charsections_dbc e emotetextsound_dbc. As duas sao lidas em
       DBCStores.cpp (LoadDBCStores), e a primeira aparece antes, entao todo
       crash dump acusa so ela - resolver uma sem a outra apenas troca o nome
       na proxima tentativa.

       As tabelas podem ficar VAZIAS. O DBCStorageBase::LoadFromDB cai de volta
       no .dbc do cliente quando a tabela nao tem linha nenhuma, que e o
       comportamento normal para essas duas.

    2) O authserver sobe e se desliga sozinho:

        No valid realms specified.

       E a tabela realmlist vazia. O 08-set-realm-address.ps1 fazia um UPDATE
       ... WHERE id = 1, que em tabela vazia atualiza zero linhas sem erro
       nenhum - o script dizia "realm configurado" e o authserver continuava
       caindo. Hoje ele insere; este script tambem repara quem ja passou por
       aquela versao.

    Nada aqui e destrutivo: as tabelas usam CREATE TABLE IF NOT EXISTS e o
    realm so e inserido quando nao ha nenhum. Pode rodar quantas vezes quiser.

.PARAMETER Address
    Endereco do realm, se ele precisar ser criado. Padrao: RealmAddress do
    settings.psd1.

.EXAMPLE
    .\scripts\fix-database.ps1
#>
[CmdletBinding()]
param(
    [string]$Address,
    [string]$Name,
    [int]$Port = 8085
)

. "$PSScriptRoot\lib\common.ps1"

$settings = Import-ServerSettings
$m        = $settings.MySql

if (-not $Address) { $Address = $settings.RealmAddress }
if (-not $Name)    { $Name    = $settings.RealmName }

if (-not (Test-MySqlReachable -Settings $settings)) {
    Write-Fail "o MySQL nao esta respondendo em $($m.Host):$($m.Port)." `
               'Rode .\scripts\start-mysql.ps1 (como Administrador).'
}

$consertos = 0

# --- 1) tabelas que o loader consulta e o SQL do core nao cria ---------------
Write-Step "Tabelas de DBC que faltam em $($m.WorldDb)"

# Os nomes das colunas nao podem ser inventados: o DBCDatabaseLoader::Load le
# por POSICAO e conta um caractere do formato para cada coluna do SELECT *.
# Coluna a mais ou a menos e crash de novo, agora sem mensagem util.
#
#   charsections_dbc   <- CharSectionsEntry,     fmt "diiixxxiii"
#   emotetextsound_dbc <- EmotesTextSoundEntry,  fmt "niiii"
#
# Os 'x' sao campos que o servidor nao le, mas a coluna tem que existir mesmo
# assim - eles contam na conta do formato.
$tabelas = [ordered]@{
    'charsections_dbc' = @'
CREATE TABLE IF NOT EXISTS `charsections_dbc` (
  `ID` int NOT NULL DEFAULT '0',
  `Race` int NOT NULL DEFAULT '0',
  `Gender` int NOT NULL DEFAULT '0',
  `GenType` int NOT NULL DEFAULT '0',
  `TexturePath1` varchar(100) DEFAULT NULL,
  `TexturePath2` varchar(100) DEFAULT NULL,
  `TexturePath3` varchar(100) DEFAULT NULL,
  `Flags` int NOT NULL DEFAULT '0',
  `Type` int NOT NULL DEFAULT '0',
  `Color` int NOT NULL DEFAULT '0',
  PRIMARY KEY (`ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
'@
    'emotetextsound_dbc' = @'
CREATE TABLE IF NOT EXISTS `emotetextsound_dbc` (
  `ID` int NOT NULL DEFAULT '0',
  `EmotesTextId` int NOT NULL DEFAULT '0',
  `RaceId` int NOT NULL DEFAULT '0',
  `SexId` int NOT NULL DEFAULT '0',
  `SoundId` int NOT NULL DEFAULT '0',
  PRIMARY KEY (`ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
'@
}

foreach ($nome in $tabelas.Keys) {
    $existe = Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                           -Database $m.WorldDb -Sql "SHOW TABLES LIKE '$nome';" -Quiet

    if ($existe -match [regex]::Escape($nome)) {
        Write-Ok "$nome ja existe"
        continue
    }

    Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                 -Database $m.WorldDb -Sql $tabelas[$nome] | Out-Null

    Write-Ok "$nome criada (vazia, como deve ser)"
    $consertos++
}

# --- 2) realm nenhum cadastrado ---------------------------------------------
Write-Step "Realm em $($m.AuthDb)"

$temTabela = Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                          -Database $m.AuthDb -Sql "SHOW TABLES LIKE 'realmlist';" -Quiet

if ($temTabela -notmatch 'realmlist') {
    Write-Warn "a tabela realmlist ainda nao existe em $($m.AuthDb)"
    Write-Info 'suba o servidor uma vez para ele popular os bancos e rode este script de novo'
} else {
    $quantos = (Invoke-MySql -Settings $settings -User $m.User -Password $m.Password `
                             -Database $m.AuthDb -Sql 'SELECT COUNT(*) FROM realmlist;' -Quiet)
    $quantos = ([regex]::Match($quantos, '\d+')).Value

    if ($quantos -and [int]$quantos -gt 0) {
        Write-Ok "ja ha $quantos realm(s) cadastrado(s)"
    } else {
        $safeName = $Name -replace "'", "''"
        $safeAddr = $Address -replace "'", "''"

        Invoke-MySql -Settings $settings -User $m.User -Password $m.Password -Database $m.AuthDb -Sql @"
INSERT INTO realmlist (id, name, address, localAddress, localSubnetMask, port)
VALUES (1, '$safeName', '$safeAddr', '127.0.0.1', '255.255.255.0', $Port)
ON DUPLICATE KEY UPDATE name = VALUES(name), address = VALUES(address), port = VALUES(port);
"@ | Out-Null

        Write-Ok "realm '$Name' criado em $Address`:$Port"
        Write-Info 'era isso que fazia o authserver desligar com "No valid realms specified."'
        $consertos++
    }
}

Write-Step 'Resultado'
if ($consertos -eq 0) {
    Write-Ok 'nada a corrigir - o banco ja esta como deveria'
} else {
    Write-Ok "$consertos correcao(oes) aplicada(s)"
    Write-Info 'suba o servidor de novo: .\scripts\start-server.ps1'
}

exit 0
