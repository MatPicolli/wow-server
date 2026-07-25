# Depois que estiver rodando

---

## Comandos de GM

Digite no chat do jogo com `.` na frente, ou na janela do worldserver sem o
ponto.

Seu personagem precisa estar numa conta com `gmlevel 3`.

### Ligar o modo GM

```
.gm on          # imune, invisível pros mobs
.gm fly on      # voar em qualquer lugar
.gm visible off # sumir pros outros jogadores
```

### Personagem

```
.level up 79            # sobe 79 níveis
.modify money 10000000  # 1000 ouro (o valor é em cobre)
.modify speed 5         # 5x mais rápido (1 = normal)
.modify hp 100
.maxskill               # todas as skills no máximo pro nível atual
.learn all my spells    # todas as magias da classe
.learn all my talents
```

### Itens

```
.additem 49623          # Shadowmourne, pelo ID
.additem 6948 5         # 5 Pedras de Regresso
.lookup item pedra      # descobrir o ID pelo nome
```

IDs de itens: <https://www.wowhead.com/wotlk/items> — o número está na URL.

### Se locomover

```
.tele stormwind
.tele orgrimmar
.lookup tele dala       # procura destino
.go xyz 1630 240 61     # coordenadas
.recall                 # volta pro último lugar antes do .tele
```

### Mobs e NPCs

```
.npc add 448            # spawna o NPC de ID 448 onde você está
.npc info               # info do que está selecionado
.die                    # mata o alvo
.revive                 # ressuscita você ou o alvo
```

### Servidor

```
.server info
.announce Reiniciando em 5 minutos
.server shutdown 300    # desliga em 300s, salvando tudo
.saveall
```

Lista completa: <https://www.azerothcore.org/wiki/gm-commands>

---

## Contas

Na janela do **worldserver**:

```
account create usuario senha
account set gmlevel usuario 3 -1
account set password usuario nova nova
account delete usuario
account onlinelist
```

Níveis de GM: `0` jogador, `1` moderador, `2` game master, `3` administrador.

---

## Jogar de outro PC

### Na mesma rede (LAN)

1. Descubra o IP local da máquina do servidor:
   ```powershell
   (Get-NetIPAddress -AddressFamily IPv4 |
     Where-Object { $_.InterfaceAlias -notmatch 'Loopback' }).IPAddress
   ```

2. Aponte o realm pra ele:
   ```powershell
   .\scripts\08-set-realm-address.ps1 -Address 192.168.0.10
   ```

3. Libere as portas no firewall (PowerShell como Administrador):
   ```powershell
   New-NetFirewallRule -DisplayName "WoW authserver" -Direction Inbound `
       -LocalPort 3724 -Protocol TCP -Action Allow
   New-NetFirewallRule -DisplayName "WoW worldserver" -Direction Inbound `
       -LocalPort 8085 -Protocol TCP -Action Allow
   ```

4. No outro PC, o `realmlist.wtf` do client aponta pro mesmo IP:
   ```
   set realmlist 192.168.0.10
   ```

### Pela internet

Mesma coisa, mas com o IP público e port forwarding no roteador (3724 e 8085
TCP). Se seu IP muda, use um DNS dinâmico (DuckDNS, No-IP) e ponha o hostname
no lugar do IP.

Antes de expor pra fora:

- troque a senha do usuário `acore` no `settings.psd1` **e** no banco — o
  padrão `acore/acore` é público
- **não** exponha a porta 3306 do MySQL
- confira que o `root` do MySQL tem senha forte

Um servidor pra você não precisa disso. Se for só seu, deixe em
`127.0.0.1` — é a configuração mais segura que existe.

---

## Backup

O que importa é o banco. Os binários e os dados extraídos você refaz.

```powershell
$data = Get-Date -Format 'yyyy-MM-dd'
mysqldump -u acore -pacore --databases acore_characters acore_auth `
    --single-transaction --result-file="C:\backups\wow-$data.sql"
```

`acore_characters` tem seus personagens; `acore_auth` tem as contas.
`acore_world` é conteúdo do jogo e é recriado pelo updater — não precisa
guardar.

Pra restaurar:

```powershell
mysql -u root -p < C:\backups\wow-2026-07-24.sql
```

Dá pra agendar isso no Agendador de Tarefas do Windows.

---

## Módulos

O AzerothCore tem uma coleção grande de módulos: bots que jogam com você,
transmog, sistema de solo, XP configurável, etc.

<https://github.com/azerothcore/azerothcore-wotlk/wiki/Catalogue-of-modules>

O modelo é: clonar dentro de `modules/` e recompilar.

```powershell
cd C:\AzerothCore\source\modules
git clone https://github.com/azerothcore/mod-eluna.git

cd C:\AzerothCore\wow-server
.\scripts\03-build.ps1
.\scripts\06-deploy.ps1
.\scripts\start-server.ps1
```

Vários módulos trazem SQL próprio e um `.conf.dist` que precisa virar `.conf`
em `server\configs\`. Leia o README de cada um.

Pra jogar sozinho, o mais popular é o
[mod-playerbots](https://github.com/liyunfan1223/mod-playerbots) — bots que
formam grupo e raide com você. Ele fica num fork do core, então exige um clone
diferente; siga o README dele.

### Rendimento de profissões (minério, erva, pesca, couro)

O `worldserver.conf` controla **chance** de drop, não **quantidade**. Quanto
cada nódulo de minério ou erva entrega está no `MinCount`/`MaxCount` das
tabelas de loot do banco `acore_world`. Tem um script pra isso:

```powershell
# preview — não altera nada
.\scripts\tune-professions.ps1 -Mining 3 -Herbalism 3

# aplicar
.\scripts\tune-professions.ps1 -Mining 3 -Herbalism 3 -Apply

# tudo (mineração, herbalismo, pesca, esfolamento) em dobro
.\scripts\tune-professions.ps1 -All 2 -Apply

# voltar ao original
.\scripts\tune-professions.ps1 -Reset -Apply
```

Com `-Mining 3`, um veio de cobre que dava 2–4 passa a dar 6–12.

Depois de aplicar, recarregue sem reiniciar — no console do worldserver:

```
reload gameobject_loot_template
reload fishing_loot_template
reload skinning_loot_template
```

**Por que ele não acumula:** os valores originais são copiados pra tabela
`custom_gather_backup` na primeira execução, e todo cálculo parte dela. Rodar
`-Mining 3` duas vezes continua sendo 3x, não 9x; trocar depois pra `-Mining 2`
recalcula em cima do original. O `-Reset` restaura os valores exatos e
descarta o backup.

O SQL gerado é salvo em `data/sql/custom/db_world/tune_professions.sql`, que o
auto-updater aplica sozinho — então a customização sobrevive a recriar o banco
do zero.

Mineração e herbalismo saem os dois de `gameobject_loot_template` (os nódulos
são gameobjects do tipo baú); o script separa um do outro pela classe do item
(7/7 = metal e pedra, 7/9 = erva). Baús comuns e o resto do loot não são
tocados.

### Ajustes sem módulo

Muita coisa está no `worldserver.conf`:

```ini
Rate.XP.Kill    = 5      # 5x de XP por mob
Rate.XP.Quest   = 5
Rate.Drop.Money = 3
Rate.Rest.InGame = 5
GM.LoginState   = 2      # entra sempre com o modo GM ligado
```

Depois de mexer, reinicie o worldserver.

---

## Atualizar o AzerothCore

```powershell
.\scripts\stop-server.ps1
.\scripts\02-clone-source.ps1     # git pull
.\scripts\03-build.ps1            # recompila só o que mudou
.\scripts\06-deploy.ps1
.\scripts\start-server.ps1        # aplica os SQLs novos sozinho
```

Faça backup dos bancos antes. Os `.conf` não são sobrescritos pelo
`06-deploy.ps1` — mas quando uma atualização adiciona opções novas, elas
aparecem só no `.conf.dist`. De vez em quando vale comparar os dois:

```powershell
$c = 'C:\AzerothCore\server\configs'
Compare-Object (Get-Content "$c\worldserver.conf") (Get-Content "$c\worldserver.conf.dist")
```

---

## Addons

Funcionam normalmente — é só copiar pra `Interface\AddOns\` do client. Use
addons da era WotLK (3.3.5a).

<https://github.com/topics/wotlk-addon> tem uma coleção mantida.
