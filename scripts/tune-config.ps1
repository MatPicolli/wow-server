<#
.SYNOPSIS
    Ajusta valores do worldserver.conf, preservando comentarios.

.DESCRIPTION
    Multiplicadores de XP, velocidade de movimento, ganho de pericia de
    profissao e companhia moram no worldserver.conf, nao no banco. Este script
    troca esses valores sem regerar o arquivo - ele e quase todo comentario
    explicativo, e o AzerothCore documenta cada chave ali dentro.

    Os valores originais de cada chave alterada sao guardados uma unica vez em
    configs\.tune-config-original.json. Por isso aplicar 3 duas vezes continua
    sendo 3, e -Reset devolve exatamente o que estava antes da primeira
    alteracao - a mesma propriedade dos ajustes de banco.

    Preview por padrao. Sem -Apply nada e gravado.

.PARAMETER Setting
    Pares chave=valor. Para varios, separe por virgula - o PowerShell recusa o
    mesmo parametro repetido:

        -Setting Rate.XP.Kill=3,Rate.XP.Quest=3      correto
        -Setting Rate.XP.Kill=3 -Setting ...         erro

.PARAMETER Reset
    Devolve todas as chaves alteradas aos valores originais.

.PARAMETER List
    Mostra o valor atual das chaves informadas (ou de todas as ja alteradas).

.PARAMETER Apply
    Grava. Sem isso, so mostra o que faria.

.EXAMPLE
    .\scripts\tune-config.ps1 -Setting Rate.XP.Kill=3,Rate.MoveSpeed.Player=1.5
    .\scripts\tune-config.ps1 -Setting Rate.XP.Kill=3,Rate.MoveSpeed.Player=1.5 -Apply

.EXAMPLE
    .\scripts\tune-config.ps1 -Reset -Apply
#>
[CmdletBinding()]
param(
    [string[]]$Setting = @(),
    [switch]$Reset,
    [switch]$List,
    [switch]$Apply
)

. "$PSScriptRoot\lib\common.ps1"

$settings   = Import-ServerSettings
$configsDir = Join-Path $settings.ServerDir 'configs'
$confPath   = Join-Path $configsDir 'worldserver.conf'
$backupPath = Join-Path $configsDir '.tune-config-original.json'

if (-not (Test-Path $confPath)) {
    Write-Fail "nao achei $confPath." 'Rode .\scripts\07-configure.ps1 primeiro.'
}

# --- leitura -----------------------------------------------------------------
$texto = [IO.File]::ReadAllText($confPath)

function Get-ConfValue {
    <#
        Le o valor de uma chave.

        O padrao termina em (?=\r?\n|$) e nao em '$': em modo multiline o '$'
        casa antes do \n e deixa o \r do CRLF de fora, entao um padrao ancorado
        com '$' nao casa em arquivo escrito no Windows - que e o caso deste.
    #>
    param([string]$Texto, [string]$Chave)

    $re = [regex]::new('(?m)^(?<lead>[ \t]*' + [regex]::Escape($Chave) + '[ \t]*=[ \t]*)(?<val>[^\r\n#]*?)(?<tail>[ \t]*(?:#[^\r\n]*)?)(?=\r?\n|$)')
    $m = $re.Match($Texto)
    if (-not $m.Success) { return $null }
    return $m.Groups['val'].Value.Trim()
}

function Set-ConfValue {
    param([string]$Texto, [string]$Chave, [string]$Valor)

    $re = [regex]::new('(?m)^(?<lead>[ \t]*' + [regex]::Escape($Chave) + '[ \t]*=[ \t]*)(?<val>[^\r\n#]*?)(?<tail>[ \t]*(?:#[^\r\n]*)?)(?=\r?\n|$)')
    if (-not $re.IsMatch($Texto)) { return $null }

    return $re.Replace($Texto, {
        param($m)
        $m.Groups['lead'].Value + $Valor + $m.Groups['tail'].Value
    }, 1)
}

# --- backup dos valores originais --------------------------------------------
$originais = @{}
if (Test-Path $backupPath) {
    try {
        $lido = Get-Content $backupPath -Raw | ConvertFrom-Json
        foreach ($p in $lido.PSObject.Properties) { $originais[$p.Name] = $p.Value }
    } catch {
        Write-Warn "nao consegui ler $backupPath - ele sera refeito"
    }
}

# --- -List -------------------------------------------------------------------
if ($List) {
    $chaves = if ($Setting.Count -gt 0) {
        $Setting | ForEach-Object { $_ -split ',' } |
            Where-Object { $_.Trim() } |
            ForEach-Object { (($_ -split '=', 2)[0]).Trim() }
    } else {
        @($originais.Keys | Sort-Object)
    }

    Write-Step 'Valores atuais'
    if (@($chaves).Count -eq 0) { Write-Info 'nenhuma chave alterada ate agora'; exit 0 }

    foreach ($c in $chaves) {
        $atual = Get-ConfValue -Texto $texto -Chave $c
        if ($null -eq $atual) { Write-Warn "$c nao existe neste worldserver.conf"; continue }
        $orig = if ($originais.ContainsKey($c)) { " (original: $($originais[$c]))" } else { '' }
        Write-Info ("{0,-34} {1}{2}" -f $c, $atual, $orig)
    }
    exit 0
}

# --- montar a lista de alteracoes --------------------------------------------
$mudancas = @()

if ($Reset) {
    if ($originais.Count -eq 0) {
        Write-Info 'nada foi alterado ate agora - nao ha o que restaurar'
        exit 0
    }
    foreach ($c in ($originais.Keys | Sort-Object)) {
        $atual = Get-ConfValue -Texto $texto -Chave $c
        $mudancas += [pscustomobject]@{ Chave = $c; De = $atual; Para = $originais[$c] }
    }
} else {
    if ($Setting.Count -eq 0) {
        Write-Fail 'informe pelo menos um ajuste.' `
                   'Ex.: .\scripts\tune-config.ps1 -Setting Rate.XP.Kill=3'
    }

    # Cada elemento e quebrado por virgula tambem. Chamado com -File - que e
    # como a GUI roda os scripts - o PowerShell entrega "a=1,b=2" como UMA
    # string, nao como array; chamado do console, ja vem separado. Quebrar aqui
    # faz as duas formas funcionarem igual.
    $pares = @()
    foreach ($item in $Setting) {
        foreach ($p in ($item -split ',')) {
            if ($p.Trim()) { $pares += $p.Trim() }
        }
    }

    foreach ($par in $pares) {
        $partes = $par -split '=', 2
        if ($partes.Count -ne 2) {
            Write-Fail "ajuste mal formado: '$par'." 'Use chave=valor, ex.: Rate.XP.Kill=3'
        }

        $chave = $partes[0].Trim()
        $valor = $partes[1].Trim()

        # A chave vira parte de um regex e de um arquivo de configuracao; e o
        # valor entra numa linha inteira. Nada de quebra de linha em nenhum dos
        # dois.
        if ($chave -notmatch '^[A-Za-z][A-Za-z0-9._]*$') {
            Write-Fail "nome de chave invalido: '$chave'."
        }
        if ($valor -notmatch '^-?[0-9]+(\.[0-9]+)?$') {
            Write-Fail "valor invalido para $chave`: '$valor'." 'Use um numero, ex.: 3 ou 1.5'
        }

        $atual = Get-ConfValue -Texto $texto -Chave $chave
        if ($null -eq $atual) {
            Write-Fail "a chave '$chave' nao existe em $confPath." `
                       'Confira o nome. O servidor ignora chave desconhecida, entao o ajuste nao teria efeito nenhum.'
        }

        $mudancas += [pscustomobject]@{ Chave = $chave; De = $atual; Para = $valor }
    }
}

# --- previa ------------------------------------------------------------------
Write-Step 'Alteracoes'
$efetivas = @()
foreach ($m in $mudancas) {
    if ($m.De -eq $m.Para) {
        Write-Info ("{0,-34} {1}  (ja esta assim)" -f $m.Chave, $m.Para)
        continue
    }
    Write-Info ("{0,-34} {1}  ->  {2}" -f $m.Chave, $m.De, $m.Para)
    $efetivas += $m
}

if ($efetivas.Count -eq 0) {
    Write-Ok 'nada a mudar'
    exit 0
}

Write-Info ''
Write-Info 'estes valores so passam a valer quando o worldserver for reiniciado'

if (-not $Apply) {
    Write-Step 'Isto foi so uma previa'
    Write-Info 'para gravar, repita o comando com  -Apply'
    exit 0
}

# --- aplicar -----------------------------------------------------------------
$novo = $texto
foreach ($m in $efetivas) {
    # O original so e guardado na PRIMEIRA vez que a chave muda. Sem isso,
    # aplicar duas vezes gravaria o valor ja alterado como "original" e o
    # -Reset deixaria de funcionar.
    if (-not $Reset -and -not $originais.ContainsKey($m.Chave)) {
        $originais[$m.Chave] = $m.De
    }

    $r = Set-ConfValue -Texto $novo -Chave $m.Chave -Valor $m.Para
    if ($null -eq $r) { Write-Fail "nao consegui gravar $($m.Chave)." 'Nada foi alterado.' }
    $novo = $r
}

if ($Reset) { $originais = @{} }

# Conferencia antes de gravar: se alguma chave nao ficou com o valor pedido, o
# arquivo nao e tocado. Melhor falhar do que deixar o conf pela metade.
foreach ($m in $efetivas) {
    $conferido = Get-ConfValue -Texto $novo -Chave $m.Chave
    if ($conferido -ne $m.Para) {
        Write-Fail "a alteracao de $($m.Chave) nao ficou como esperado ('$conferido')." 'Nada foi gravado.'
    }
}

# Copia de seguranca do arquivo inteiro, uma por dia, antes da primeira escrita.
$copia = Join-Path $configsDir ("worldserver.conf.bak-" + (Get-Date -Format 'yyyy-MM-dd'))
if (-not (Test-Path $copia)) {
    Copy-Item $confPath $copia
    Write-Info "copia de seguranca: $copia"
}

# Write-TextFileNoBom: BOM quebra o parser de config do AzerothCore.
Write-TextFileNoBom -Path $confPath -Content $novo

if ($originais.Count -gt 0) {
    Write-TextFileNoBom -Path $backupPath -Content ($originais | ConvertTo-Json)
} elseif (Test-Path $backupPath) {
    Remove-Item $backupPath -Force
}

Write-Ok "$($efetivas.Count) ajuste(s) gravado(s) em $confPath"
Write-Step 'Proximo passo'
Write-Info 'reinicie o worldserver para os valores valerem'
exit 0
