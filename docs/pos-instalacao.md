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
.\scripts\backup-db.ps1
```

Salva em `C:\AzerothCore\backups\wow-<data>.sql` e mantém os 10 mais recentes.
**Pode rodar com o servidor ligado** — o `--single-transaction` tira um retrato
consistente sem travar as tabelas.

```powershell
.\scripts\backup-db.ps1 -IncludeWorld     # inclui suas customizações de itens/NPCs/loot
.\scripts\backup-db.ps1 -KeepLast 30
```

`acore_characters` tem seus personagens; `acore_auth` tem as contas. Esses dois
são insubstituíveis. `acore_world` é conteúdo do jogo, recriado pelo updater —
só vale guardar se você já customizou coisas direto no banco.

Pra restaurar (com o servidor **desligado**):

```powershell
.\scripts\stop-server.ps1
mysql -u root -p < C:\AzerothCore\backups\wow-2026-07-25_1430.sql
```

### Automatizando

No Agendador de Tarefas do Windows, ou via PowerShell como Administrador:

```powershell
$acao = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument '-NoProfile -ExecutionPolicy Bypass -File "C:\Users\Mateus\Documents\Projetos\AI\wow-server\scripts\backup-db.ps1"'
$gatilho = New-ScheduledTaskTrigger -Daily -At 4am
Register-ScheduledTask -TaskName 'Backup WoW' -Action $acao -Trigger $gatilho
```

---

## Módulos

O AzerothCore tem uma coleção grande de módulos: bots que jogam com você,
transmog, sistema de solo, XP configurável, etc.

<https://github.com/azerothcore/azerothcore-wotlk/wiki/Catalogue-of-modules>

O modelo é: clonar dentro de `modules/` e recompilar.

```powershell
cd C:\AzerothCore\source\modules
git clone https://github.com/azerothcore/mod-eluna.git

cd C:\Users\Mateus\Documents\Projetos\AI\wow-server
.\scripts\rebuild.ps1 -Start
```

O `rebuild.ps1` lista os módulos instalados e encadeia build → deploy →
configure. É o ciclo que você repete a cada módulo adicionado, atualizado ou
removido.

**O SQL dos módulos é aplicado sozinho.** Muitos READMEs ainda mandam importar
na mão — isso é instrução antiga. O auto-updater do worldserver detecta e
aplica o SQL do módulo no primeiro start depois de instalado. A janela vai
cuspir linhas de update; é normal.

Os `.conf.dist` dos módulos também são tratados: o `06-deploy.ps1` copia e o
`07-configure.ps1` gera os `.conf` correspondentes em
`server\configs\modules\`. Os valores padrão funcionam; revise conforme o
README de cada módulo.

Para atualizar todos os módulos de uma vez:

```powershell
Get-ChildItem C:\AzerothCore\source\modules -Directory | ForEach-Object {
    git -C $_.FullName pull
}
.\scripts\rebuild.ps1
```

## Bots e casa de leilões

Existem duas categorias, com custos de instalação bem diferentes.

### Casa de leilões — módulo simples

[**mod-ah-bot**](https://github.com/azerothcore/mod-ah-bot), da org oficial.
Popula a AH com itens e compra o que os jogadores colocam à venda, então o
leilão deixa de ser um deserto num servidor de uma pessoa só.

```powershell
cd C:\AzerothCore\source\modules
git clone https://github.com/azerothcore/mod-ah-bot.git

cd C:\Users\Mateus\Documents\Projetos\AI\wow-server
.\scripts\rebuild.ps1
```

Ele precisa de uma **conta e um personagem dedicados** — é essa identidade que
aparece como vendedora. Crie no console do worldserver:

```
account create ahbot umasenhaqualquer
```

Entre com ela uma vez, crie um personagem, saia. Depois pegue os IDs:

```sql
SELECT id FROM acore_auth.account WHERE username = 'AHBOT';
SELECT guid, name FROM acore_characters.characters WHERE account = <id_acima>;
```

E preencha em `server\configs\modules\mod_ahbot.conf`. O personagem não é pra
ser jogado.

### Bots que jogam — exigem fork do core

Estes **não são módulos**: mexem no core e por isso vivem em repositórios
próprios.

| | O que é | Repositório / branch |
|---|---|---|
| **Playerbots** | Bots que agem como personagens reais: fazem quest, sobem de nível, entram no seu grupo e raide. Dá pra logar seus próprios alts como bots. | [`mod-playerbots/azerothcore-wotlk`](https://github.com/mod-playerbots/azerothcore-wotlk) branch `Playerbot` + módulo [`mod-playerbots`](https://github.com/mod-playerbots/mod-playerbots) |
| **NPCBots** | Companheiros contratados como NPC. Mais leve, menos "vivo" — não fazem quest sozinhos. | [`trickerer/AzerothCore-wotlk-with-NPCBots`](https://github.com/trickerer/AzerothCore-wotlk-with-NPCBots) branch `npcbots_3.3.5` |

São **mutuamente exclusivos** — cada um é um fork diferente do core.

Pra trocar, edite o `config\settings.psd1`:

```powershell
SourceRepository = 'https://github.com/mod-playerbots/azerothcore-wotlk.git'
SourceBranch     = 'Playerbot'
```

E então:

```powershell
.\scripts\02-clone-source.ps1 -Force   # apaga e clona o fork
cd C:\AzerothCore\source\modules
git clone https://github.com/mod-playerbots/mod-playerbots.git

cd C:\Users\Mateus\Documents\Projetos\AI\wow-server
.\scripts\rebuild.ps1 -Clean -Start
```

O `-Force` é obrigatório ao trocar de repositório: sem ele o script se recusa
a atualizar um checkout que aponta pra outro lugar, justamente pra não te
devolver ao core oficial sem avisar.

**O que você perde e o que mantém ao trocar de fork:**

- Refaz: clone (~1,3 GB) e compilação — junto, algo em torno de 20 minutos.
- **Mantém**: os dados extraídos do client (`dbc`, `maps`, `vmaps`, `mmaps`).
  São dados do seu WoW, não do core — as horas de mmaps não se repetem.
- **Mantém**: seus personagens. O auto-updater aplica o schema novo por cima
  do banco existente. Ainda assim, backup antes:
  ```powershell
  mysqldump -u acore -pacore --databases acore_characters acore_auth `
      --single-transaction --result-file=C:\backups\antes-do-fork.sql
  ```

### Rendimento de profissões (minério, erva, pesca, couro)

O `worldserver.conf` controla **chance** de drop, não **quantidade**. Quanto
cada nódulo de minério ou erva entrega está no `MinCount`/`MaxCount` das
tabelas de loot do banco `acore_world`. Tem um script pra isso:

```powershell
# preview — não altera nada
.\scripts\tune-professions.ps1 -Mining 3 -Herbalism 3

# aplicar
.\scripts\tune-professions.ps1 -Mining 3 -Herbalism 3 -Apply

# tudo em dobro
.\scripts\tune-professions.ps1 -All 2 -Apply

# voltar ao original
.\scripts\tune-professions.ps1 -Reset -Apply
```

Com `-Mining 3`, um veio de cobre que dava 2–4 passa a dar 6–12.

| Parâmetro | Tabela | O que muda |
|---|---|---|
| `-Mining` | `gameobject_loot_template` (itens 7/7) | minério e pedra por nódulo |
| `-Herbalism` | `gameobject_loot_template` (itens 7/9) | ervas por nódulo |
| `-Fishing` | `fishing_loot_template` | peixes por fisgada |
| `-Skinning` | `skinning_loot_template` | couro por esfolamento |
| `-Disenchanting` | `disenchant_loot_template` | pó, essências e fragmentos |
| `-Milling` | `milling_loot_template` | pigmentos por moagem |
| `-Prospecting` | `prospecting_loot_template` | gemas por prospecção |

Depois de aplicar, recarregue sem reiniciar — o script diz quais tabelas
recarregar no console do worldserver, por exemplo:

```
reload gameobject_loot_template
reload disenchant_loot_template
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
tocados. As outras cinco têm tabela própria, então não precisam de filtro.

### Velocidade de subir a profissão

Quantidade é uma coisa; **ganho de perícia** é outra, e fica no
`worldserver.conf`:

```ini
SkillGain.Gathering = 1    # mineração, herbalismo, esfolamento
SkillGain.Crafting  = 1    # profissões de produção

SkillChance.Orange = 100   # chance de subir por dificuldade da receita
SkillChance.Yellow = 75
SkillChance.Green  = 25
SkillChance.Grey   = 0

SkillChance.MiningSteps   = 0   # 0 = a chance não cai conforme você sobe
SkillChance.SkinningSteps = 0
```

Subir `SkillGain.Gathering` para 5 faz cada coleta valer 5 pontos de perícia.
Reiniciar o worldserver para valer.

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
