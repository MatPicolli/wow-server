# Erros comuns

---

## Build

### `Could NOT find Boost`

Quase sempre é o `BOOST_ROOT` com barras invertidas.

```powershell
[Environment]::SetEnvironmentVariable('BOOST_ROOT', 'C:/local/boost_1_86_0', 'Machine')
```

Barras **normais**, sem barra no fim. Feche e reabra o PowerShell depois.

Se persistir, confira que você baixou o binário do toolset certo:
`msvc-14.3` para Visual Studio 2022.

### `Could NOT find OpenSSL`, erro de link em `libcrypto`, ou "não gerou libcrypto-3-x64.dll"

Você tem o OpenSSL **4.x** ou a versão **Light** instalada. O AzerothCore
linka contra `libcrypto-3-x64.dll` e `libssl-3-x64.dll`, que só existem na
linha **3.x**. Confira:

```powershell
Test-Path 'C:\Program Files\OpenSSL-Win64\bin\libcrypto-3-x64.dll'
```

Se der `False`, **instale na mão** — o winget quase sempre falha aqui:

1. Abra <https://slproweb.com/products/Win32OpenSSL.html>
2. Baixe a **"Win64 OpenSSL v3.x.x"** — a linha 3, **sem ser *Light***.
   Ignore qualquer 4.x.
3. Na instalação, quando perguntar onde copiar as DLLs, escolha
   **"The OpenSSL binaries (/bin) directory"** — não o diretório de sistema.
4. Confirme de novo com o `Test-Path` acima.

Se preferir sem instalador, a FireDaemon publica builds do OpenSSL 3 em zip,
com headers e libs:
<https://kb.firedaemon.com/support/solutions/articles/4000121705>.
Descompacte e passe `-DOPENSSL_ROOT_DIR=<pasta>` no CMake.

### Por que o winget não resolve isso

Três coisas se somam:

- `ShiningLight.OpenSSL.Dev` já avançou pra **4.x**, que não tem as DLLs que o
  AzerothCore linka.
- **Não existe** `ShiningLight.OpenSSL.LTS.Dev`. A linha LTS do winget só
  publica a variante *Light*, que vem sem os headers de desenvolvimento.
- As manifests 3.x que sobraram (3.5.4, 3.6.0, 3.6.1, 3.6.2) apontam pra
  instaladores que o slproweb **já removeu do ar** — pedir uma delas dá
  `404` no meio do download:
  ```
  Downloading https://slproweb.com/download/Win64OpenSSL-3_6_2.msi
  An unexpected error occurred while executing the command:
  Download request status is not success.
  0x80190194 : Não encontrado (404).
  ```

O `01-install-prereqs.ps1` tenta essas versões da mais nova pra mais antiga e,
se todas derem 404, imprime o passo a passo manual. Se descobrir uma 3.x que
ainda baixa, dá pra passar direto:

```powershell
.\scripts\01-install-prereqs.ps1 -OpenSslVersions '3.6.1'
```

### `cannot open input file 'libmysql.lib'`

Faltam os Development Components do MySQL. Reabra o MySQL Installer →
Modify → inclua o **MySQL Server** completo.

Confira: `C:\Program Files\MySQL\MySQL Server 8.4\lib\libmysql.lib`

### `Compatibility with CMake < 3.5 has been removed from CMake`

Você está no CMake 4.x. Ele deixou de aceitar projetos que pedem
`cmake_minimum_required` abaixo de 3.5.

O `CMakeLists.txt` principal do AzerothCore declara `3.16...3.22`, então passa
— mas alguma dependência embutida pode não passar. O `03-build.ps1` detecta
isso e refaz a configuração automaticamente com:

```
-DCMAKE_POLICY_VERSION_MINIMUM=3.5
```

Se você estiver chamando o `cmake` na mão, acrescente esse flag.

Continuando a falhar, use o último CMake 3.x (3.31.x) em vez do 4.x:

```powershell
winget install --id Kitware.CMake --exact --version 3.31.6
```

### O build falha em um projeto aleatório e roda de novo passa

Acontece com paralelismo alto. Rode de novo:

```powershell
.\scripts\03-build.ps1
```

Só o que faltou é recompilado. Se repetir sempre no mesmo projeto, aí é erro
de verdade — leia a primeira mensagem de erro, não a última.

### Trocou de versão do MySQL/OpenSSL/Boost e o CMake ignora

O cache do CMake guarda os caminhos antigos:

```powershell
.\scripts\03-build.ps1 -Clean
```

---

## Extração dos dados

### `mapextractor.exe` fecha na hora / não acha os MPQ

Ele tem que rodar **de dentro da pasta do client**, com o `Data\` cheio de
`.MPQ` ao lado. O `05-extract-client-data.ps1` cuida disso; se estiver fazendo
na mão, confira o diretório atual.

### Os mmaps estão demorando 6 horas

É o normal. São 230+ mapas com pathfinding recalculado. Enquanto isso a máquina
fica pesada.

Pra usar menos núcleos e conseguir trabalhar em paralelo:

```powershell
.\scripts\05-extract-client-data.ps1 -Only mmaps -MmapThreads 4
```

Ou pule por enquanto (`-SkipMmaps` no `setup-all.ps1`) e gere outro dia.

### Acabou o espaço em disco durante a extração

A pasta `Buildings` chega a dezenas de GB. Ela só serve pra montar os vmaps —
depois que a pasta `vmaps` estiver pronta, pode apagar:

```powershell
Remove-Item -Recurse -Force "<client>\Buildings"
```

---

## Servidor não sobe

### `worldserver.exe` abre e fecha na hora, sem mensagem

Faltam DLLs. Rode `.\scripts\06-deploy.ps1` de novo, ou confira que estão em
`C:\AzerothCore\server\`:

- `libmysql.dll`
- `libcrypto-3-x64.dll`
- `libssl-3-x64.dll`

Pra ver o erro de verdade, rode pelo PowerShell em vez de dar duplo-clique — a
mensagem fica na tela.

### Não sei a senha do root do MySQL / `Access denied for user 'root'`

Acontece quando o MySQL foi instalado em modo silencioso (via `winget`), sem o
assistente de configuração ter rodado.

Primeiro veja o erro exato:

```powershell
& 'C:\Program Files\MySQL\MySQL Server 8.4\bin\mysql.exe' `
    -u root -p -h 127.0.0.1 --protocol=TCP -e "SELECT 1;"
```

- **`Access denied`** → o servidor está de pé, a senha é que está errada.
  Siga o reset abaixo.
- **`Can't connect`** → o serviço não está rodando ou está em outra porta.
  `Get-Service MySQL*` e `Start-Service <nome>`.

#### Resetando a senha do root

O passo que quase todo tutorial erra: o `mysqld.exe` precisa do
**`--defaults-file`**. Sem ele, o servidor ignora o `my.ini` e tenta usar
`C:\Program Files\MySQL\MySQL Server 8.4\data\`, que não existe — o datadir
real fica em `C:\ProgramData\`. O sintoma é abortar em menos de um segundo:

```
[ERROR] [MY-013276] Failed to set datadir to
'C:\Program Files\MySQL\MySQL Server 8.4\data\' (OS errno: 2 - No such file or directory)
```

Descubra o `my.ini` que o serviço realmente usa:

```powershell
Get-CimInstance Win32_Service -Filter "Name='MySQL84'" |
    Select-Object -ExpandProperty PathName
```

A saída traz o `--defaults-file=...`. Use esse caminho (PowerShell como
Administrador):

```powershell
Stop-Service MySQL84

Set-Content -Path C:\mysql-reset.txt -Encoding ASCII `
    -Value "ALTER USER 'root'@'localhost' IDENTIFIED BY 'SuaSenhaNova';"

& 'C:\Program Files\MySQL\MySQL Server 8.4\bin\mysqld.exe' `
    --defaults-file="C:\ProgramData\MySQL\MySQL Server 8.4\my.ini" `
    --init-file=C:\mysql-reset.txt --console
```

Espere aparecer **`ready for connections`** — se sair em menos de um segundo,
o caminho do `--defaults-file` está errado. Então `Ctrl+C` e:

```powershell
Remove-Item C:\mysql-reset.txt
Start-Service MySQL84
```

Alternativa sem linha de comando: abra o **MySQL Installer** → **Reconfigure**
no MySQL Server e defina a senha pelo assistente.

### `Could not connect to MySQL database` / `Access denied for user 'acore'`

1. O serviço do MySQL está rodando? `Get-Service MySQL*`
2. Usuário e senha do `worldserver.conf` batem com o `settings.psd1`?
3. Teste na mão:
   ```powershell
   mysql -u acore -pacore -h 127.0.0.1 --protocol=TCP acore_auth -e "SELECT 1;"
   ```

Se der "Access denied" só via `127.0.0.1` mas funcionar via `localhost`, é o
`skip-name-resolve`. Rode `.\scripts\04-setup-database.ps1` de novo — ele cria
o usuário pros dois hosts.

### Trava em `Loading world information...`

`DataDir` errado no `worldserver.conf`. Ele tem que apontar pra pasta que
contém `dbc\` e `maps\`:

```ini
DataDir = "C:/AzerothCore/server/Data"
```

O log logo acima costuma dizer qual arquivo ele não achou.

### O worldserver reclama de `vmaps` ou `mmaps` sem parar

Você não extraiu esses dados mas as opções estão ligadas. Ou extraia, ou
desligue no `worldserver.conf`:

```ini
vmap.enableLOS    = 0
vmap.enableHeight = 0
MoveMaps.Enable   = 0
```

O `07-configure.ps1` já ajusta isso conforme o que existe em `Data\`.

---

## Client não conecta

### "Não foi possível conectar" na tela de login

O **authserver** não está rodando, ou o `realmlist.wtf` está errado.

```powershell
Get-Process authserver
Get-Content "<client>\Data\enUS\realmlist.wtf"   # deve ser: set realmlist 127.0.0.1
```

Cuidado com o locale: se seu client é `ptBR`, o arquivo que vale é
`Data\ptBR\realmlist.wtf`.

E abra o **`Wow.exe`**, não o `Launcher.exe` — o launcher sobrescreve o
realmlist.

### Login passa, mas trava em "Conectando..." / lista de realms vazia

A tabela `realmlist` do banco está com endereço errado. Ela é quem diz ao
client onde achar o worldserver:

```powershell
.\scripts\08-set-realm-address.ps1
```

De outro PC da rede, use o IP local da máquina do servidor — não `127.0.0.1`:

```powershell
.\scripts\08-set-realm-address.ps1 -Address 192.168.0.10
```

### "Suas informações de conta estão incorretas"

- A senha do 3.3.5a tem **limite de 16 caracteres** e ignora maiúsculas.
- A conta foi criada mesmo? Na janela do worldserver:
  ```
  account onlinelist
  ```
  Pra recriar a senha:
  ```
  account set password meuuser novasenha novasenha
  ```

### "Esta versão do World of Warcraft foi desativada"

Seu client não é build **12340**. O AzerothCore só aceita essa.

```powershell
(Get-Item "<client>\Wow.exe").VersionInfo.FileVersion
```

Tem que terminar em `12340`.

---

## PowerShell

### "a execução de scripts foi desabilitada neste sistema"

```powershell
Set-ExecutionPolicy Bypass -Scope Process
```

Vale só pra janela atual — é o suficiente e não mexe na política da máquina.

Note que `Bypass` é o **valor** do parâmetro `-ExecutionPolicy`, e não um
switch. Escrever `-Scope Process -Bypass` devolve *"Não é possível localizar
um parâmetro que coincida com o nome de parâmetro 'Bypass'"*.

Se você clonou o repo com `git clone`, normalmente nem precisa: a política
padrão `RemoteSigned` só barra script **baixado da internet** e não assinado.
Arquivos que vieram do git não têm a marca de origem (mark-of-the-web) que o
navegador ou o `Invoke-WebRequest` colocam, então rodam direto. Quem baixa o
`.zip` pelo GitHub, sim, esbarra no bloqueio — nesse caso dá pra liberar
arquivo por arquivo com `Unblock-File`:

```powershell
Get-ChildItem -Recurse -Filter *.ps1 | Unblock-File
```

### `Import-PowerShellDataFile : não é reconhecido`

PowerShell muito antigo. Precisa da versão 5.1+ (padrão no Windows 10/11):

```powershell
$PSVersionTable.PSVersion
```

### O script diz que falta uma dependência que eu acabei de instalar

Feche e reabra o PowerShell. Variáveis de ambiente (`PATH`, `BOOST_ROOT`) só
entram em sessões novas.
