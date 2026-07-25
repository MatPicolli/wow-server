# Servidor de WoW 3.3.5a (AzerothCore) — setup no Windows

Kit de scripts pra levantar um servidor privado de World of Warcraft
**Wrath of the Lich King (3.3.5a, build 12340)** compilando o
[AzerothCore](https://github.com/azerothcore/azerothcore-wotlk) do código-fonte,
no Windows.

Você já tem o client. Isso aqui faz o resto.

---

## O que vai acontecer

| # | Etapa | Tempo |
|---|-------|-------|
| 1 | Instalar dependências (VS 2022, CMake, MySQL, OpenSSL 3.x, Boost) | 30–60 min |
| 2 | Clonar o código-fonte (~2 GB) | 5–15 min |
| 3 | Compilar | 15–60 min |
| 4 | Criar os bancos de dados | 1 min |
| 5 | **Extrair os dados do client** (dbc, maps, vmaps, mmaps) | **1–6 horas** |
| 6 | Montar a pasta do servidor | 1 min |
| 7 | Configurar | 1 min |
| 8 | Primeiro start (popula os bancos) | ~10 min |

O gargalo é a etapa 5 — os **mmaps** (dados de pathfinding) levam horas porque
são calculados mapa por mapa. Dá pra pular e gerar depois; veja
[Sem tempo agora](#sem-tempo-agora).

**Espaço em disco:** ~60 GB pro fonte + build, ~25 GB pro servidor pronto.

---

## Começando

### 1. Configure

```powershell
git clone https://github.com/matpicolli/wow-server.git
cd wow-server

Copy-Item config\settings.example.psd1 config\settings.psd1
notepad config\settings.psd1
```

A única coisa que você **precisa** mudar é o `ClientDir` — a pasta onde está o
`Wow.exe`:

```powershell
ClientDir = 'D:\Games\World of Warcraft 3.3.5a'
```

O resto tem padrões que funcionam.

> `settings.psd1` está no `.gitignore` porque guarda senhas.

### 2. Libere a execução de scripts (só se precisar)

Se você clonou com `git clone`, provavelmente **não precisa disso** — a
política padrão (`RemoteSigned`) só bloqueia script baixado da internet, e
arquivos vindos do git não carregam essa marca.

Se der "a execução de scripts foi desabilitada neste sistema", libere só
nesta janela:

```powershell
Set-ExecutionPolicy Bypass -Scope Process
```

> `Bypass` é o **valor** do `-ExecutionPolicy`, não um switch —
> `-Scope Process -Bypass` dá erro de parâmetro.

### 3. Instale as dependências

**Como Administrador** (só precisa uma vez):

```powershell
.\scripts\01-install-prereqs.ps1
```

Depois **feche e reabra o PowerShell** — as variáveis de ambiente novas
(`BOOST_ROOT`, `PATH` do MySQL) só aparecem numa sessão nova.

Se ele não conseguir instalar alguma coisa sozinho, ele te diz o link pra
baixar na mão. Veja também [docs/passo-a-passo.md](docs/passo-a-passo.md) pra
instalação 100% manual.

### 4. Confira

```powershell
.\scripts\00-check-prereqs.ps1
```

Isso descobre o que falta em 5 segundos, em vez de você esperar 30 minutos de
compilação pra tomar erro.

### 5. Rode tudo

```powershell
.\scripts\setup-all.ps1
```

Ele encadeia as etapas 2 a 7. Vai demorar horas, quase tudo nos mmaps.
Pode parar com Ctrl+C e retomar — cada etapa pula o que já está pronto.

Ou rode uma a uma, se preferir acompanhar:

```powershell
.\scripts\02-clone-source.ps1
.\scripts\03-build.ps1
.\scripts\04-setup-database.ps1
.\scripts\05-extract-client-data.ps1
.\scripts\06-deploy.ps1
.\scripts\07-configure.ps1
```

### 6. Primeiro start

```powershell
.\scripts\start-server.ps1
```

Abrem duas janelas: **authserver** (login) e **worldserver** (o mundo).

No primeiro start o worldserver aplica todos os SQLs nos bancos vazios. A tela
fica cuspindo linhas por uns 10 minutos — é normal, **não feche**. Espere
aparecer o prompt `AC>`.

### 7. Crie sua conta

Na janela do **worldserver**, com o `AC>` na tela:

```
account create meuuser minhasenha
account set gmlevel meuuser 3 -1
```

`3` é o nível máximo de GM. O `-1` aplica em todos os realms.

> A senha do WoW 3.3.5a não diferencia maiúsculas de minúsculas e tem limite de
> 16 caracteres.

### 8. Ajuste o realm e entre

```powershell
.\scripts\08-set-realm-address.ps1
```

Agora abra o `Wow.exe` (**não** o Launcher) e logue com a conta que você criou.

---

## Sem tempo agora

Pra pular os mmaps e jogar hoje:

```powershell
.\scripts\setup-all.ps1 -SkipMmaps
```

O servidor sobe e funciona. A diferença é que os mobs andam em linha reta —
atravessam parede, não contornam obstáculo, ficam presos em escada. Gera os
mmaps depois, quando o PC estiver livre:

```powershell
.\scripts\05-extract-client-data.ps1 -Only mmaps
.\scripts\07-configure.ps1        # religa o MoveMaps.Enable
```

---

## Uso no dia a dia

```powershell
.\scripts\start-server.ps1        # sobe
.\scripts\stop-server.ps1         # desce (limpo, salva os personagens)
.\scripts\stop-server.ps1 -Force  # mata o processo — só se travar
```

Pra atualizar o AzerothCore depois:

```powershell
.\scripts\02-clone-source.ps1     # git pull
.\scripts\03-build.ps1            # recompila só o que mudou
.\scripts\06-deploy.ps1           # copia os binários novos
.\scripts\start-server.ps1        # o worldserver aplica os SQLs novos sozinho
```

Comandos de GM, jogar de outro PC, addons e mods:
[docs/pos-instalacao.md](docs/pos-instalacao.md).

---

## Estrutura

```
config/
  settings.example.psd1     modelo — copie pra settings.psd1
scripts/
  lib/common.ps1            funções compartilhadas
  00-check-prereqs.ps1      diagnóstico, não instala nada
  01-install-prereqs.ps1    instala as dependências (Admin)
  02-clone-source.ps1       clona/atualiza o AzerothCore
  03-build.ps1              CMake + MSVC
  04-setup-database.ps1     cria usuário e bancos
  05-extract-client-data.ps1  dbc, maps, vmaps, mmaps
  06-deploy.ps1             binários + DLLs -> ServerDir
  07-configure.ps1          .conf + realmlist.wtf do client
  08-set-realm-address.ps1  tabela realmlist (rodar após o 1º start)
  setup-all.ps1             encadeia 00 -> 07
  start-server.ps1
  stop-server.ps1
docs/
  passo-a-passo.md          instalação manual, sem os scripts
  pos-instalacao.md         GM, LAN/internet, mods, backup
  troubleshooting.md        erros comuns
```

Nada do servidor em si mora neste repo — o código do AzerothCore, os binários
compilados e os dados do client ficam nas pastas do `settings.psd1`
(`C:\AzerothCore\...` por padrão). Aqui só ficam os scripts.

---

## Requisitos

Instalados pelo `01-install-prereqs.ps1`:

- Windows 10 ou 11
- Visual Studio 2022 Community + workload "Desenvolvimento para desktop com C++"
- CMake ≥ 3.27
- MySQL ≥ 8.0 (recomendado 8.4), **com os Development Components**
- **OpenSSL 3.x** — a 4.x **não** serve, o AzerothCore linka contra
  `libcrypto-3-x64.dll`
- Boost ≥ 1.78, binários pré-compilados pra MSVC 14.3
- Client de WoW 3.3.5a, build **12340**

---

## Legalidade

O AzerothCore é software livre (GPL/AGPL) e emula o servidor a partir de
engenharia reversa — não contém código nem assets da Blizzard. Rodar um
servidor privado pra uso pessoal com um client que você já possui é o caso de
uso normal do projeto.

O que os EULAs da Blizzard vedam é distribuir o client, cobrar por acesso ou
operar servidor público. Servidor local, só seu, é outra conversa.

---

## Fontes

- [AzerothCore — Installation](https://www.azerothcore.org/wiki/installation)
- [AzerothCore — Windows Requirements](https://www.azerothcore.org/wiki/windows-requirements)
- [AzerothCore — Windows Core Installation](https://www.azerothcore.org/wiki/windows-core-installation)
- [AzerothCore — Windows Server Setup](https://www.azerothcore.org/wiki/windows-server-setup)
- [AzerothCore — Extracting DBC, Maps, VMaps & MMaps](https://www.azerothcore.org/wiki/Extract-Client-Data)
- [AzerothCore — GM Commands](https://www.azerothcore.org/wiki/gm-commands)
