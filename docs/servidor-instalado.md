# AzerothCore 3.3.5a — o servidor instalado em `D:\AzerothCore`

> **De onde veio este documento.** Outro agente trabalhou direto na pasta do
> servidor, no Windows, e escreveu este relato do que mudou lá. Ele entrou aqui
> como está, sem reescrita, porque é o registro dele — nada abaixo foi
> verificado nesta máquina.
>
> **Ele descreve a instalação, não este repositório.** O `CLAUDE.md` da raiz
> continua sendo o guia dos scripts e da GUI. Quando os dois falarem do mesmo
> assunto, aqui vale para o que está rodando em `D:\AzerothCore`, e lá vale para
> o que o código deste repositório faz.
>
> Nada do que está descrito aqui — o SQL do prestígio, os scripts Lua, os
> `.bat`, o set de herança de 48 peças, o `Item.dbc` remendado — mora neste
> repositório. Está tudo na máquina do Mateus. Se um dia isso precisar
> sobreviver a uma formatação, o caminho é trazer esses arquivos para cá, não
> recriá-los a partir deste texto.

Instruções para o agente que mexe naquela instalação. **Leia antes de mexer em qualquer coisa.**

Servidor **pessoal, single-player**, com playerbots fazendo o papel de população. Não há
outros jogadores reais. Isso muda as prioridades: estabilidade e conveniência valem mais
que balanceamento competitivo ou proteção contra abuso.

Este documento registra tudo que foi descoberto na marra. A seção **Armadilhas** é a mais
valiosa — são erros que já custaram tempo e que se repetem se você não souber deles.

---

## 1. Identidade

| Item | Valor |
|---|---|
| Core | AzerothCore, branch **Playerbot** |
| Revisão | `ceeb3116e` (2026-07-24) |
| Build | Windows, Visual Studio 17 2022, `RelWithDebInfo`, `MODULES=static` |
| Install prefix | `D:/AzerothCore/server` |
| Cliente | WotLK 3.3.5a, **na mesma máquina** que o servidor |
| Hardware | 6–8 núcleos, 32 GB |
| Banco | MySQL 8.4 — `acore_auth`, `acore_world`, `acore_characters` |

Credenciais: leia de `server/configs/worldserver.conf` (`LoginDatabaseInfo` etc.).
**Não replique a senha em documentação nova.**

### Layout

```
D:\AzerothCore\
├── source/          fonte do core + source/modules/
├── build/           saída do CMake/VS
├── server/          worldserver.exe, configs/, Data/dbc/, lua_scripts/, logs/
├── backups/         dumps .sql e backups de DBC
├── PrestigeDraft/   SQL, arquivos de cliente e .bat do mod de prestígio
└── *.bat *.sql *.md utilitários e guias
```

### Módulos instalados (10)

`mod-account-achievements`, `mod-ah-bot`, `mod-ale`, `mod-aoe-loot`, `mod-autobalance`,
`mod-guildhouse`, `mod-instance-reset`, `mod-playerbots`, `mod-solo-lfg`, `mod-solocraft`.

**`mod-ale`** é o motor Lua (Eluna renomeado). Todo script em `server/lua_scripts/` depende dele.

Já foram **removidos**: `mod-assistant`, `mod-reagent-bank`, `mod-npc-beastmaster`,
`mod-learn-spells`, `mod-dynamic-xp`.

---

## 2. Armadilhas — leia antes de escrever SQL ou Lua

### 2.1 O banco do mundo está num schema MAIS ANTIGO que o `source`

A pegadinha que mais custou tempo nesta sessão. Exemplos confirmados:

| Tabela | `source/data/sql/base/` | Banco real |
|---|---|---|
| `creature` | `id1`, `id2`, `id3` | **`id`** |
| `creature_template` | tem `scale`, `trainer_*`, `mechanic_immune_mask`, `spell_school_immune_mask` | **não tem essas 7 colunas** |

**Nunca valide SQL de terceiros contra `source/data/sql/base/`.** Valide contra o banco vivo.
O jeito barato é extrair os `CREATE TABLE` do dump mais recente em `backups/`:

```bash
awk '/^CREATE TABLE `creature` \(/,/^\) ENGINE/' backups/<dump>.sql
```

Melhor ainda: extraia **todas** as tabelas do dump de uma vez para um dicionário e valide
todos os `INSERT` de um mod numa passada só, em vez de descobrir um erro por execução —
foi assim que a validação do Prestige saiu de 4 rodadas de tentativa e erro para uma.

Sintomas de ignorar: `ERROR 1054 Unknown column 'X' in 'field list'`.

Arquivos já corrigidos por causa disso: `chromie_spawn.sql`, `cleanup_modulos_removidos.sql`.

### 2.2 SQL de terceiros raramente é idempotente

Padrão observado em **três** mods diferentes: `CREATE TABLE IF NOT EXISTS` seguido de
`INSERT INTO` puro. Roda uma vez, quebra na segunda com `ERROR 1062 Duplicate entry`.

**Regra da casa: todo SQL deste repositório precisa poder rodar duas vezes.**

- Linhas: `DELETE` antes do `INSERT`, ou `INSERT IGNORE` / `REPLACE INTO`
- Colunas: MySQL 8 não tem `ADD COLUMN IF NOT EXISTS`. Use o procedure que consulta o
  `information_schema` — padrão em `PrestigeDraft/SQL/Acore_characters/prestige_corrigido.sql`

### 2.3 Bugs reais encontrados no SQL do mod Prestige

Vale conhecer porque indicam o nível de cuidado esperado ao aplicar mods de terceiros:

- **`prestige.sql`**: o `CREATE TABLE` de `prestige_stats` **não** cria 5 colunas que o
  código usa (`bans`, `taskmaster_state`, `tasks_completed`, `taskmaster_mode`, `on_task`).
  Elas só existem num bloco `ALTER` marcado como "update", que na verdade é obrigatório
  mesmo em instalação nova.
- O mesmo `ALTER` tem **erro de sintaxe**: `;` no lugar de `,` antes do `ADD COLUMN on_task`,
  o que faz aplicar 4 das 5 colunas e abortar. Substituído por `prestige_corrigido.sql`.
- **`chromie_spawn.sql`**: spawns com guid fixo e `INSERT` puro, sem `DELETE`.
- **`prestige_draft_specific_tables.sql`**: `INSERT INTO` em tabelas `dbc_*` criadas com
  `IF NOT EXISTS`. Convertido para `REPLACE INTO`.

### 2.4 Adicionar módulo exige re-rodar o CMake

Não basta buildar. É preciso **Configure + Generate** no CMake para ele enxergar a pasta
nova em `source/modules/`, e depois buildar o projeto **INSTALL** (não o `worldserver`
direto — o INSTALL é o que copia binários e `.conf.dist` para `server/`).

Sintoma de esquecer: build passa, servidor sobe, módulo simplesmente não existe.

### 2.5 Armadilhas de `.bat` no Windows

Todas já morderam nesta sessão:

- `title Foo & Bar` — o `&` separa comandos. Escape: `^&`
- `for %%F in (` com a lista quebrada em várias linhas **não funciona**. O laço não é
  reconhecido e `%%F` sai literal como `%F`. Use uma linha só ou uma sub-rotina por arquivo
- `mysqldump` no MySQL 8 precisa de `--no-tablespaces`, senão falha por falta da permissão
  `PROCESS`

Checklist antes de entregar um `.bat`: `&` escapado no title, nenhum `for /in` multilinha,
parênteses balanceados, todo `goto` com label existente.

### 2.6 O log do ALE é cego por padrão

`mod_ale.conf` define `Logger.ALE`, mas **isso nunca é aplicado**: o sistema de log do
AzerothCore é inicializado *antes* de "Loading Modules Configuration..." no boot. Só vale o
que está no `worldserver.conf`.

Resultado: nenhum `ALE.log` era criado e erros de Lua caíam no `Logger.root` (nível 2),
sumindo. **Isso escondeu dois bugs por várias rodadas.** Já corrigido — existe agora em
`worldserver.conf`:

```
Logger.ALE=4,Console Server Errors
```

Se erros de Lua voltarem a sumir, confira essa linha primeiro.

---

## 3. Convenções

### Scripts `.bat`

Todo `.bat` que toca o banco segue esta forma:

1. Avisa para parar o worldserver e dá `pause`
2. Testa `mysql` no PATH e a conexão
3. **Faz `mysqldump` antes de qualquer escrita e aborta se o backup falhar ou vier pequeno**
4. Aplica cada arquivo via sub-rotina, parando no primeiro erro
5. Termina com consultas de verificação que provam que deu certo

O backup que aborta na falha é o que torna seguro rodar coisas destrutivas.

### Edições em arquivo de terceiro

Marque com `-- [ajuste local]` ou `-- [custom]`, explicando **o erro que aquilo evita**.
Quando o mod for atualizado, esses comentários são o que permite reaplicar as correções.

---

## 4. IDs reservados — não reutilize

| Faixa | Uso |
|---|---|
| `item_template` 700000–700047 | set de herança completo (48 peças) |
| `creature_template` 2069426 | Chromie, NPC de prestígio |
| `creature` guid 5300512 / 5300513 | spawns da Chromie via SQL (Ironforge / Orgrimmar) |
| `creature` guid 5300696–5300698 | Chromies extras adicionadas com `.npc add` |
| `gameobject_template` 500030 | vendedor de guild house (precisa `.npc add`) |
| gossip intid 400–440, 500–599 | menus customizados da Chromie |

---

## 5. Configurações aplicadas

### AH Bot

Estava carregando mas inerte, com `AHBot: Account id and player id missing from configuration`
no log. Precisa de uma conta com personagem.

| Config | Valor | Motivo |
|---|---|---|
| `AuctionHouseBot.Account` | 102 | conta `ahbot` dedicada |
| `EnableSeller` / `EnableBuyer` | 1 / 1 | comprador é essencial em server solo, senão você nunca vende nada |
| `ItemsPerCycle` | 500 | enche mais rápido |
| `ConsiderOnlyBotAuctions` | 1 | seus leilões não contam contra a cota do bot |
| `DuplicatesCount` | 3 | evita 50 stacks do mesmo item |
| `ElapsingTimeClass` | 0 | leilões de 1–3 dias, mercado estável |
| `mod_auctionhousebot.min/maxitems` | 1000 | nas 3 casas (auctionhouse 2, 6, 7) |

O código só consulta `SELECT guid FROM characters WHERE account = <Account>` —
`Player::Initialize` instancia o Player em memória sem ler o banco, então **raça, classe e
facção do personagem do bot são irrelevantes** e um personagem só atende as três casas.

### Playerbots

| Config | Valor | Motivo |
|---|---|---|
| `Min/MaxRandomBots` | 1200 | |
| `RandomBotAccountCount` | 0 | **0 significa automático, não desligado** — ver 6.1 |
| `BotActiveAlone` | 10 | só ~10% recebem IA completa por vez, em rodízio |
| `BotActiveAloneForceWhenInZone` | **0** | era 1; forçar todo bot da zona ignora o limite de 10% e faz o custo escalar direto com o total justamente nas capitais |
| `RandomBotsPerInterval` | 100 | não subir mais: valor alto vira pico de CPU a cada 20s, ruim com o cliente na mesma máquina |
| `MapUpdate.Threads` | 3 | cliente na mesma máquina precisa de ~1,5 núcleo, MySQL ~1 |
| `ProcessPriority` | **0 (Normal)** | era 1 (HIGH); o worldserver preemptava o cliente e causava travadinha |

`botActiveAloneSmartScale` está ligado (floor 50ms / ceiling 200ms): reduz sozinho a fatia
ativa quando o tick do servidor sobe. É uma rede de segurança — o servidor degrada suave em
vez de travar. Por isso dá pra experimentar mais bots sem medo.

### Reset de talentos grátis e sem NPC

- `NoResetTalentsCost = 1` no `worldserver.conf` — zera a taxa e a escalada (1g → 5g → 10g)
- `rbac_linked_permissions (195, 716)` — libera `.reset talents` para contas **sem** gmlevel

A cadeia: `rbac_default_permissions` dá a permissão 195 ao secId 0 (Player); 195 é o papel
"Role: Sec Level Player", expandido via `rbac_linked_permissions`; 716 é
"Command: reset talents". Pendurar 716 no papel 195 libera para todos. O grupo `reset` não
tem permissão própria, só os subcomandos, então a 716 sozinha basta.

Aplicar com `reset_talents_para_jogadores.sql` e depois `.reload rbac` (não precisa reiniciar).

**Ressalva:** o handler aceita alvo opcional, então quem tem a permissão pode resetar
talentos de outro personagem selecionado, inclusive de playerbots.

---

## 6. Verificação — não confie, meça

Hábitos que pegaram erro real nesta sessão:

### 6.1 Leia o log, não só o `.conf`

`AiPlayerbot.RandomBotAccountCount = 0` parecia "bots desligados". O `Playerbots.log` mostrava:

```
Found 100 total randombot accounts in database
Account type assignment complete: 50 RNDbot accounts, 50 AddClass accounts
```

`0` significa **automático**. Quase reportei o oposto.

### 6.2 Confira a assinatura do hook e o call site no core antes de usar

Dois casos concretos, ambos evitaram bugs sérios:

- **Hook 61 `ON_BEFORE_UPDATE_SKILL`** parece o caminho óbvio para multiplicar profissão
  ("Can return new amount"), mas o `value` é o valor **absoluto** da skill, não o ganho
  (`PlayerUpdates.cpp:722` e `:938`). Multiplicar por 3 saltaria Mineração de 150 para 450.
  O correto é o hook **62**, que dispara depois do skill-up.
- **`Player:EquipItem`** com um **número** chama `Item::CreateItem` e cria uma peça nova.
  Combinado com `AddItem`, o jogador recebe duas. Passe o **objeto Item**.
  O `GiveStartingGear` do próprio mod tem esse bug — já corrigido aqui.

### 6.3 Confira a assinatura dos métodos do ALE

`Player:SendAddonMessage(prefix, message, channel, receiver)` — o 4º argumento
(`CHECKOBJ<Player>(L, 5)`) é **obrigatório**. Sem ele o Lua estoura antes de enviar e nada
acontece, silenciosamente. Foi o que quebrou o ícone de buff por uma rodada.

### 6.4 Valide Lua sem executar

```bash
pip install lupa --break-system-packages
python3 -c "import lupa; L=lupa.LuaRuntime(); print(L.eval('function(s) local f,e=load(s); return e end')(open('arquivo.lua').read()))"
```

Compila e retorna o erro de sintaxe sem rodar nada. Vale rodar em **todos** os scripts após
qualquer edição — pegou um `titulo` não declarado que teria quebrado em runtime.

### 6.5 Em SQL gerado por script, confira contagem de colunas e aspas

`len(colunas) == len(valores)` em cada linha, e número par de aspas simples no arquivo.

---

## 7. Customizações ativas

### 7.1 Prestige & Draft Mode (mod Lua de terceiro)

Fonte: <https://github.com/Youpeoples/Prestige-and-Draft-Mode> (MIT, Eluna/Lua).

- `CONFIG.MAX_LEVEL = 30` em `prestige_and_spell_choice_config.lua`. **Apesar do nome, é o
  nível MÍNIMO para prestigiar.** Vinha 70 de fábrica. Só é usado como porta em
  `prestige_chromie.lua`; o `total_expected_drafts` do draft usa o nível real do personagem,
  então baixar não quebra a matemática do draft.
- Em Draft Mode o personagem **não tem talentos**: `goodbye_talentpoints_draft.lua` zera os
  pontos no login e a cada level. Não está documentado no README do mod.
- O mod **devolve todo o equipamento pelo correio** ao prestigiar. Isso é relevante: as peças
  de herança voltam sozinhas a cada ciclo.
- Gold **não** é perdido ao prestigiar.

Instalação e passos em `PRESTIGE-PASSOS.md`. SQL aplicado por `PrestigeDraft/aplicar_sql.bat`.

### 7.2 Bônus permanentes de prestígio — `server/lua_scripts/prestige_bonuses.lua`

Multiplicadores lidos de `acore_characters.prestige_stats`, com cache em memória por GUID.

| Bônus | Regra | Hook |
|---|---|---|
| XP de mobs e quests | `min(1 + prestige_level, 10)` | 12 |
| Reputação 3x | só com `maxlevel_prestige = 1` | 15 |
| Profissão 3x | idem | **62** (não 61 — ver 6.2) |
| Honra 3x | idem, **só abate de PvP** | 6 |
| Materiais em dobro | idem | 32 |

**Não são auras, e isso é deliberado.** O pedido era "permanente, nem na morte". Aura é
removível, dispelável e some em vários casos. Lido do banco, só some se o personagem for
deletado — tratado no hook 2 (`ON_CHARACTER_DELETE`, que recebe o guid como número).

**Detalhes de implementação que não são óbvios:**

- **XP**: o hook 12 dispara para `XPSOURCE_KILL` (0), `QUEST` (1), `QUEST_DF` (2),
  `EXPLORE` (3) e `BATTLEGROUND` (4). Os call sites são `KillRewarder.cpp:184`,
  `PlayerQuest.cpp:759`, `Player.cpp:5838` e `:6245`. O script filtra para kill e quest.
- **Reputação**: só multiplica quando `incremental` é true. Com false o core está
  **definindo** um valor absoluto, e multiplicar corromperia o standing.
- **Honra é parcial por limitação do ALE**: não existe hook de honra. O contorno lê os
  pontos, espera 250 ms via `CreateLuaEvent` e soma o dobro do ganho. Pega abate de jogador,
  **não** pega honra de objetivo nem de vitória de battleground.
- **Materiais**: o hook 32 não permite alterar a quantidade (não aceita retorno), então
  entrega uma cópia extra via `AddItem`. O filtro é `class = 7` (Trade Goods) com subclasse
  em `{5 cloth, 6 leather, 7 metal/stone, 8 meat/peixe, 9 herb, 12 enchanting}` — conferidas
  item a item no `item_template` deste servidor.

### 7.3 Set de herança completo — 48 itens

O WotLK 3.3.5a só tem herança de ombro e peito (20% de XP). Foram criados 8 slots novos
(Helm, Pendant, Bracers, Gloves, Belt, Leggings, Boots, Cloak) × 6 variantes = **48 itens**,
entries **700000–700047**, cada um com o feitiço **57353** (+10% XP). Total: **100%**.

Fórmula: `entry = 700000 + (índiceDoSlot * 6) + índiceDaVariante`

Variantes (mesma ordem das ombreiras originais, e os templates de onde foram clonadas):

| Índice | Nome | Armadura/papel | Template |
|---|---|---|---|
| 0 | Polished … of Valor | placa, força | 42949 |
| 1 | Champion's … | malha, físico | 42950 |
| 2 | Mystical … of Elements | malha, conjurador | 42951 |
| 3 | Stained Shadowcraft … | couro, agilidade | 42952 |
| 4 | Preened Ironfeather … | couro, conjurador | 42984 |
| 5 | Tattered Dreadmist … | tecido, conjurador | 42985 |

**Por que empilha:** auras de **itens diferentes** com o mesmo feitiço empilham. O core tem
comentário explícito em `SpellAuras.cpp:1963`:

```cpp
// Exception: item-sourced auras from different items can stack
if (!(GetCastItemGUID() && existingAura->GetCastItemGUID() && GetCastItemGUID() != existingAura->GetCastItemGUID()))
    return false;
```

É o mesmo motivo de ombro + peito darem 20% hoje.

**Stats escalam de verdade** via `ScalingStatDistribution` (reusada da ombreira equivalente)
e `ScalingStatValue`, cujo bitmask está em `DBCStructure.h`:

- orçamento de stats: `0x1` ombro, `0x8` peito
- armadura tier ombro: `0x20` tecido, `0x40` couro, `0x80` malha, `0x100` placa
- armadura tier peito: `0x100000` … `0x800000`; capa `0x80000`

Mapeamento usado: Helm e Leggings recebem orçamento de peito (slots de orçamento alto);
Bracers, Gloves, Belt e Boots recebem o de ombro; Cloak usa ombro + bit de capa; Pendant usa
ombro sem bit de armadura.

**Ressalva registrada:** o DBC só tem dois níveis de orçamento, então braçadeiras e cinto
usam o de ombro e ficam levemente generosos. Aproximação deliberada, erra para o lado forte.

A tabela `CLASS_VARIANT` em `prestige_chromie.lua` mapeia classe → variante e foi deixada
isolada para ajuste fácil (ex.: se Caçador não conseguir equipar malha em nível baixo,
trocar de `1` para `3`).

### 7.4 As duas descobertas críticas sobre itens customizados no cliente

Estas duas custaram várias rodadas e valem para **qualquer** item novo que você criar.

#### displayid precisa estar abaixo de ~32000

Primeira tentativa usou modelos da era ICC (64190–65131). Os itens apareciam como `?`.
O `ItemDisplayInfo.dbc` do **servidor** vai até 68742, então a verificação server-side
passava — mas o **cliente** não resolve esses displayids.

Prova: as heranças originais do jogo, que aparecem corretas, usam **6337–31657**.

Regra: ao escolher displayid para item customizado, restrinja a `< 32000` e confirme que
existe no `ItemDisplayInfo.dbc` **com `InventoryIcon` preenchido**.

#### O item precisa existir no `Item.dbc` do CLIENTE

Sintomas de não estar: **sem ícone na bag**, **clique-direito não equipa**, **sem som ao
equipar** — mas o **painel de personagem mostra o ícone normalmente**, porque esse caminho
usa o displayid enviado no pacote de equipamento visível, sem consultar o `Item.dbc`.

Diagnóstico: o `Item.dbc` do cliente tem 46096 itens, ID máximo **56806**. As entries
700000+ não estão lá. Confirmado que os **únicos** 48 itens do servidor fora do `Item.dbc`
são exatamente os 48 de herança.

Solução: `PrestigeDraft/Cliente/Patch/Item.dbc` já contém os 48 registros extras
(46144 total, ordenado por ID). Precisa ser entregue ao cliente dentro de um MPQ.

Formato do `Item.dbc` (3.3.5a), 8 campos de 4 bytes:

```
0 ID | 1 ClassID | 2 SubclassID | 3 SoundOverrideSubclassID
4 MaterialID | 5 DisplayInfoID | 6 InventoryType | 7 SheatheType
```

Header DBC: `magic(4s), recordCount(i), fieldCount(i), recordSize(i), stringBlockSize(i)`,
seguido dos registros e do bloco de strings. Para regerar, use o script embutido no
histórico ou replique: ler o original, anexar os registros novos, atualizar `recordCount`,
preservar o bloco de strings.

**Limitação do ambiente:** não há escritor de MPQ disponível (o `mpyq` é só leitura). O
`instalar_addon.bat` procura o `MPQEditor.exe` do Ladik's MPQ Editor e gera o
`patch-Z.MPQ`; se não achar, avisa exatamente o que baixar.

### 7.5 Prestige Armory — loja da Chromie

Três lojas separadas no gossip: **Armor**, **Weapons**, **Trinkets and Rings**, mais o painel
**My Prestige Bonuses**.

- Vende as 48 peças customizadas **e** as heranças originais do WotLK que já existiam no
  banco (ombreira, peitoral, 15 armas, 4 trinkets, 1 anel)
- Filtro por classe (proficiência real de arma) e por facção (insígnias)
- Preço: `min(2000 + prestige_level * 250, 3000)` ouro
- Comprar grava em `acore_characters.prestige_heirloom_unlocks`, chaveada por
  **`(account_id, item_entry)`** — desbloqueio por conta, permanente
- Peça já comprada pode ser reenviada pelo correio, grátis. **Guarda:** só envia se você não
  tiver a peça (`GetItemCount`), senão cliques repetidos enchiam a caixa de correio
- `SendMail` usa `stationary = 41` (`MAIL_STATIONERY_DEFAULT`). `0` não é valor válido

O painel de bônus existe porque os bônus permanentes **não têm interface nativa** — sem ele
não há como confirmar que estão ativos. Serve também como diagnóstico.

### 7.6 Ícones de buff — addon `PrestigeBuff.lua`

Os bônus não são auras, então não aparecem na barra de buffs. Para ter ícone com tooltip
próprio ("EXP BOOST 4X"), foi preciso addon: **o cliente só conhece 12 auras de XP, todas de
+5% ou +10%** — nenhuma diz "2x", e criar feitiço customizado exigiria MPQ.

Arquitetura:

- Servidor: `PrestigeBonuses_Enviar()` manda `"<prestige_level>:<xp_mult>:<maxlevel 0|1>"`
  com prefixo `PRESTIGEBONUS`, via `SendAddonMessage(prefix, msg, 7, player)`
- Envio no login com **3 s de atraso** via `CreateLuaEvent` — no instante do login o addon
  ainda não está ouvindo
- Cliente: `PrestigeSystem/PrestigeBuff.lua` desenha dois ícones arrastáveis ao lado do
  minimapa, com `GameTooltip` customizado

Existe também `AURA_ICONE = 57819` ("Argent Champion") no `prestige_bonuses.lua`, **desligada
por padrão** (`AURA_ATIVA = false`). Era a alternativa sem addon: aura permanente com efeito
`APPLY_AURA/DUMMY`, sem as flags de passivo (`0x40`) e oculto (`0x80`) que a esconderiam da
barra. Ligue se quiser ícone na barra nativa, aceitando o nome errado.

---

## 8. Arquivos e para que servem

| Arquivo | Assunto |
|---|---|
| `CLAUDE.md` (na máquina) | a versão original deste documento |
| `AHBOT-PASSOS.md` | configuração do AH Bot |
| `PRESTIGE-PASSOS.md` | instalação do Prestige & Draft Mode |
| `ahbot_setup.bat` / `.sql` | cria personagem do bot e ajusta o conf |
| `cleanup_modulos_removidos.bat` / `.sql` | remove restos de módulos desinstalados |
| `reset_talents_para_jogadores.sql` | libera `.reset talents` via RBAC |
| `instalar_addon.bat` | **tudo do lado do cliente**: addon, patch-P, patch-Z com Item.dbc, limpa Cache |
| `PrestigeDraft/aplicar_sql.bat` | aplica todo o SQL do prestígio, com backup e verificação |
| `PrestigeDraft/SQL/Acore_characters/prestige_corrigido.sql` | substitui o `prestige.sql` quebrado do mod |
| `PrestigeDraft/SQL/Acore_characters/prestige_bonus_permanente.sql` | coluna `maxlevel_prestige` |
| `PrestigeDraft/SQL/Acore_characters/prestige_heirloom_unlocks.sql` | desbloqueios da loja |
| `PrestigeDraft/SQL/Acore_world/heranca_set_completo.sql` | os 48 itens |
| `PrestigeDraft/Cliente/Patch/Item.dbc` | Item.dbc com os 48 itens — precisa ir num MPQ |
| `server/lua_scripts/prestige_bonuses.lua` | bônus permanentes |
| `server/lua_scripts/Prestige & Draft Mode/prestige_chromie.lua` | gossip, loja, prestígio |

---

## 9. Ao adicionar um mod novo

1. Ler o README **e** listar os arquivos do repositório — mods costumam ter scripts não
   documentados (foi o caso do `goodbye_talentpoints_draft.lua`)
2. Conferir se é C++ (precisa rebuild + CMake) ou Lua (só precisa do mod-ale)
3. **Validar todo o SQL contra o dump vivo antes de rodar**, todos os arquivos de uma vez
4. Tornar o SQL idempotente
5. Fazer backup de qualquer `.dbc` antes de sobrescrever (`backups/dbc-original-*`)
6. Checar conflito com o que já existe. Precedente concreto: `mod-learn-spells` dava todas as
   magias automaticamente e teria anulado o Draft Mode inteiro — foi removido por isso
7. Se o mod criar itens, aplicar as duas regras da seção 7.4 (displayid < 32000 e entrada no
   `Item.dbc` do cliente)

---

## 10. Pendências conhecidas

- **`patch-Z.MPQ` depende do MPQEditor.exe.** Enquanto não for gerado, as 48 peças continuam
  sem ícone na bag, sem clique-direito e sem som. Todo o resto delas funciona.
- **Guild house sem NPC vendedor.** O módulo está carregado e o SQL aplicado, mas exige
  `.npc add 500030` num lugar público. Sem isso ninguém compra guild house.
- **5 Chromies spawnadas** (2 do SQL + 3 de `.npc add`). Funcionam igual, só ocupam espaço.
- **Honra 3x é parcial** — só abate de jogador, por falta de hook no ALE. Cobrir BG exigiria
  patch em C++ no core.
- **Braçadeiras e cinto levemente generosos** — limitação de dois níveis de orçamento no DBC.
