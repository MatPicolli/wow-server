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

### `Could NOT find OpenSSL` ou erro de link em `libcrypto`

Você provavelmente instalou o OpenSSL **4.x** ou a versão **Light**.

O AzerothCore precisa da linha **3.x** completa. Confira:

```powershell
Test-Path 'C:\Program Files\OpenSSL-Win64\bin\libcrypto-3-x64.dll'
```

Se der `False`, desinstale e baixe a "Win64 OpenSSL v3.x.x" (não Light) em
<https://slproweb.com/products/Win32OpenSSL.html>.

### `cannot open input file 'libmysql.lib'`

Faltam os Development Components do MySQL. Reabra o MySQL Installer →
Modify → inclua o **MySQL Server** completo.

Confira: `C:\Program Files\MySQL\MySQL Server 8.4\lib\libmysql.lib`

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
Set-ExecutionPolicy -Scope Process -Bypass
```

Vale só pra janela atual — é o suficiente e não mexe na política da máquina.

### `Import-PowerShellDataFile : não é reconhecido`

PowerShell muito antigo. Precisa da versão 5.1+ (padrão no Windows 10/11):

```powershell
$PSVersionTable.PSVersion
```

### O script diz que falta uma dependência que eu acabei de instalar

Feche e reabra o PowerShell. Variáveis de ambiente (`PATH`, `BOOST_ROOT`) só
entram em sessões novas.
