# Instalação manual, sem os scripts

Se preferir fazer na mão — ou se algum script falhar e você quiser entender o
que ele estava tentando fazer.

Os caminhos abaixo seguem os padrões do `config/settings.psd1`. Ajuste se você
mudou.

---

## 1. Dependências

### Git
<https://git-scm.com/download/win>

Na instalação, em "Adjusting your PATH environment", escolha
**"Git from the command line and also from 3rd-party software"**.

### Visual Studio 2022 Community
<https://visualstudio.microsoft.com/vs/community/>

No instalador, aba **Cargas de trabalho (Workloads)**, marque
**"Desenvolvimento para desktop com C++"**. Sem isso não existe compilador.

Não use versões *Preview*.

### CMake ≥ 3.27
<https://cmake.org/download/>

Baixe o `.msi` **windows-x86_64** da **Latest Release** — nunca um Release
Candidate. Na instalação, marque "Add CMake to the system PATH".

### MySQL 8.x
<https://dev.mysql.com/downloads/installer/>

Use o **MSI Installer**. Pontos que importam:

- inclua o **MySQL Server** e os **Development Components**
  (é o que traz `lib\libmysql.lib`, sem o qual o build falha no link)
- deixe marcado o *MySQL Instance Configuration Wizard* antes de finalizar
- anote a senha do `root`
- adicione `C:\Program Files\MySQL\MySQL Server 8.4\bin\` ao `PATH`

### OpenSSL 3.x — atenção
<https://slproweb.com/products/Win32OpenSSL.html>

Baixe a **"Win64 OpenSSL v3.x.x"**:

- **não** a versão *Light* (não tem os headers de desenvolvimento)
- **não** a linha **4.x** — o AzerothCore linka contra `libcrypto-3-x64.dll` e
  `libssl-3-x64.dll`, que a 4.x não tem. O `winget` hoje instala 4.x por
  padrão; por isso o script tenta o pacote `ShiningLight.OpenSSL.LTS.Dev`
  primeiro.

Durante a instalação, quando ele perguntar onde copiar as DLLs, escolha
**"The OpenSSL binaries (/bin) directory"** — e não o diretório de sistema do
Windows.

### Boost ≥ 1.78 (binários MSVC)
<https://archives.boost.io/release/1.86.0/binaries/>

Baixe `boost_1_86_0-msvc-14.3-64.exe` (o `14.3` é o toolset do VS 2022; o `64`
é a arquitetura). Instale em `C:\local\boost_1_86_0`.

Depois crie a variável de ambiente **de sistema**:

| Nome | Valor |
|------|-------|
| `BOOST_ROOT` | `C:/local/boost_1_86_0` |

**Use barras normais (`/`), não invertidas**, e sem barra no fim. O CMake do
AzerothCore não acha o Boost se estiver com `\`. É o erro mais comum dessa
etapa.

---

## 2. Código-fonte

```powershell
git clone https://github.com/azerothcore/azerothcore-wotlk.git C:\AzerothCore\source
```

A branch `master` é a estável do 3.3.5a.

---

## 3. Compilar

```powershell
cmake -S C:\AzerothCore\source -B C:\AzerothCore\build `
      -G "Visual Studio 17 2022" -A x64 `
      -DTOOLS_BUILD=all `
      -DSCRIPTS=static `
      -DMODULES=static `
      -DCMAKE_INSTALL_PREFIX=C:\AzerothCore\server

cmake --build C:\AzerothCore\build --config RelWithDebInfo --parallel
```

Pela GUI do CMake dá na mesma: aponte source e build, escolha o generator
"Visual Studio 17 2022" em x64, deixe `TOOLS_BUILD` em `all`, Configure →
Generate. Depois abra `AzerothCore.sln`, ponha **RelWithDebInfo** + **x64** e
Build no `ALL_BUILD`.

`TOOLS_BUILD=all` é o que compila os extractors. Sem eles você não tem como
extrair os dados do client.

Saída: `C:\AzerothCore\build\bin\RelWithDebInfo\`

Deve ter:

```
authserver.exe        worldserver.exe
map_extractor.exe     vmap4_extractor.exe
vmap4_assembler.exe   mmaps_generator.exe
configs\authserver.conf.dist
configs\worldserver.conf.dist
```

> Versões mais antigas do AzerothCore geravam esses nomes sem underscore
> (`mapextractor.exe`). Os scripts aceitam as duas grafias.

---

## 4. Bancos de dados

O AzerothCore usa três bancos. O script oficial fica em
`C:\AzerothCore\source\data\sql\create\create_mysql.sql` e cria o usuário
`acore` com senha `acore`:

```powershell
mysql -u root -p < C:\AzerothCore\source\data\sql\create\create_mysql.sql
```

Isso cria `acore_auth`, `acore_world` e `acore_characters` **vazios**. Quem
popula é o worldserver no primeiro start.

> O script oficial só concede acesso a `'acore'@'localhost'`. Isso normalmente
> funciona pra conexões em `127.0.0.1` porque o MySQL resolve o IP de volta pra
> `localhost` — mas quebra se o servidor estiver com `skip-name-resolve`. O
> `04-setup-database.ps1` cria o usuário pros dois hosts justamente pra evitar
> esse "Access denied".

---

## 5. Extrair os dados do client

Os extractors leem os `.MPQ` de `Data\` e **precisam rodar de dentro da pasta
do client**.

Copie os quatro `.exe` do `build\bin\RelWithDebInfo\` pra raiz do client (junto
do `Wow.exe`), abra um terminal ali e rode **nesta ordem**:

```powershell
# 1. dbc, maps e Cameras  (5-15 min)
.\map_extractor.exe

# 2. Buildings — matéria-prima dos vmaps  (20-40 min)
.\vmap4_extractor.exe

# 3. montar os vmaps  (5-10 min)
mkdir vmaps
.\vmap4_assembler.exe Buildings vmaps

# 4. mmaps — pathfinding  (1-6 HORAS)
mkdir mmaps
.\mmaps_generator.exe
```

Cada etapa termina quando aparece `Press any key...`. Não interrompa no meio,
principalmente os vmaps.

Depois mova as pastas geradas pro servidor:

```
C:\AzerothCore\server\Data\
    dbc\        obrigatório
    maps\       obrigatório
    vmaps\      sem isso, mobs enxergam através de parede
    mmaps\      sem isso, mobs andam em linha reta
    Cameras\    cinemáticas de introdução de raça
```

A pasta `Buildings` (dezenas de GB) só serve pra montar os vmaps — pode apagar
depois.

---

## 6. Montar a pasta do servidor

Copie pra `C:\AzerothCore\server\`:

De `build\bin\RelWithDebInfo\`:
- `authserver.exe`, `worldserver.exe` (e os `.pdb`, se quiser stack trace
  legível em caso de crash)
- a pasta `configs\` com os `.conf.dist`

Do MySQL (`MySQL Server 8.4\lib\`):
- `libmysql.dll`

Do OpenSSL (`OpenSSL-Win64\bin\`):
- `libcrypto-3-x64.dll`
- `libssl-3-x64.dll`
- `legacy.dll`, se existir (várias builds da 3.x não têm — tudo bem)

Sem essas DLLs os executáveis abrem e fecham na hora, sem mensagem nenhuma.

> Se você atualizar o MySQL depois, **recompile**. A `libmysql.dll` nova pode
> não ser compatível com o binário antigo.

---

## 7. Configurar

Em `C:\AzerothCore\server\configs\`, copie os `.dist` tirando a extensão:

```
authserver.conf.dist   ->  authserver.conf
worldserver.conf.dist  ->  worldserver.conf
```

**`authserver.conf`:**

```ini
LoginDatabaseInfo = "127.0.0.1;3306;acore;acore;acore_auth"
```

**`worldserver.conf`:**

```ini
LoginDatabaseInfo     = "127.0.0.1;3306;acore;acore;acore_auth"
WorldDatabaseInfo     = "127.0.0.1;3306;acore;acore;acore_world"
CharacterDatabaseInfo = "127.0.0.1;3306;acore;acore;acore_characters"

DataDir = "C:/AzerothCore/server/Data"
```

Formato da string: `"IP;porta;usuário;senha;banco"`.

Se você **não** extraiu vmaps/mmaps, desligue também:

```ini
vmap.enableLOS    = 0
vmap.enableHeight = 0
MoveMaps.Enable   = 0
```

Deixar ligado sem os dados faz o worldserver reclamar sem parar no log.

---

## 8. Apontar o client

Edite `Data\enUS\realmlist.wtf` dentro da pasta do client (troque `enUS` pelo
seu locale — `ptBR`, `enGB`, etc.):

```
set realmlist 127.0.0.1
```

Se existir `WTF\Config.wtf`, confira que ele não tem uma linha
`SET realmList` apontando pra outro lugar — ela ganha do `realmlist.wtf`.

---

## 9. Subir

```powershell
cd C:\AzerothCore\server
.\authserver.exe -c configs\authserver.conf
.\worldserver.exe -c configs\worldserver.conf
```

No primeiro start o worldserver aplica todos os SQLs. ~10 minutos. Espere o
prompt `AC>`.

Crie a conta na janela do worldserver:

```
account create meuuser minhasenha
account set gmlevel meuuser 3 -1
```

E ajuste o realm no banco (a tabela só existe depois do primeiro start):

```sql
UPDATE acore_auth.realmlist
   SET name = 'Meu Servidor', address = '127.0.0.1', port = 8085
 WHERE id = 1;
```

Abra o `Wow.exe` — **não** o Launcher, que sobrescreve o realmlist.
