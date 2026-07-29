# Sistema de Prestígio — AzerothCore + ALE

Mod de servidor para WoW 3.3.5a (WotLK). Permite que um jogador "prestigie": volta ao nível 1 voluntariamente em troca de um multiplicador permanente de experiência. Quem prestigia no nível máximo recebe um bônus adicional permanente.

Escrito inteiramente em Lua sobre o engine **ALE**. Não requer recompilação do core.

---

## Índice

1. [Aviso crítico](#aviso-crítico)
2. [Ambiente](#ambiente)
3. [Estrutura do repositório](#estrutura-do-repositório)
4. [Instalação](#instalação)
5. [Como o sistema funciona](#como-o-sistema-funciona)
6. [Referência de configuração](#referência-de-configuração)
7. [Arquitetura por módulo](#arquitetura-por-módulo)
8. [Hooks utilizados](#hooks-utilizados)
9. [Decisões de design e por quê](#decisões-de-design-e-por-quê)
10. [Limitações conhecidas](#limitações-conhecidas)
11. [Superfície de API não verificada](#superfície-de-api-não-verificada)
12. [Protocolo de teste](#protocolo-de-teste)
13. [Diagnóstico](#diagnóstico)
14. [Roadmap](#roadmap)

---

## Aviso crítico

**Este mod executa uma operação destrutiva e irreversível sobre personagens de jogadores.** Ele apaga nível, quests e conquistas, e remove todo o equipamento e conteúdo de mochila, reenviando por correio.

Não existe função de desfazer. O único mecanismo de recuperação é o backup do banco `acore_characters`.

Qualquer alteração nos módulos `04_prestige_wipe.lua` ou `06_prestige_maxlevel.lua` deve ser validada com o protocolo de teste completo antes de chegar a produção. Um bug na varredura de inventário se manifesta como perda permanente de itens de jogador.

---

## Ambiente

| Item | Valor |
|---|---|
| Core | AzerothCore rev. `ceeb3116ebed` (2026-07-24), branch Playerbot, Win64 RelWithDebInfo |
| Cliente | WoW 3.3.5a (build 12340), `## Interface: 30300` |
| Engine Lua | **ALE** (`azerothcore/mod-ale`) |
| Banco | MySQL 8.4.9 |
| Pasta de execução | `D:\AzerothCore\server\` |
| Pasta de scripts Lua | `D:\AzerothCore\server\lua_scripts\` |
| Config do engine | `D:\AzerothCore\server\configs\modules\mod_ale.conf` |
| Documentação da API | https://www.azerothcore.org/eluna/ |

### ALE não é Eluna

O ALE é um fork independente do Eluna, específico para AzerothCore, e **divergiu a ponto de não ser mais compatível**. Scripts escritos para Eluna não rodam no ALE e vice-versa.

Consequência prática para manutenção: a maior parte dos exemplos de Lua para AzerothCore que existe na internet é de Eluna e pode não funcionar. A única referência confiável é a documentação oficial do ALE no link acima. Ao consultar a doc, as enums de eventos ficam **dentro da página da função de registro** correspondente (`RegisterPlayerEvent`, `RegisterCreatureEvent`, etc.), não em uma página separada.

### Notas de plataforma (Windows / PowerShell)

- O operador `<` de redirecionamento de entrada **não existe no PowerShell**. Para importar SQL use `Get-Content arquivo.sql | mysql -u root -p banco`, ou `mysql ... -e "source C:/caminho/com/barras/normais.sql"`, ou `cmd /c "..."`.
- O operador `>` funciona normalmente (usado para `mysqldump`), mas grava em UTF-16 por padrão. Para texto limpo, use `Out-File -Encoding utf8`.
- Arquivos `.lua` salvos pelo Bloco de Notas podem virar `.lua.txt` (o Explorer esconde extensões conhecidas) ou ganhar BOM UTF-8, que alguns builds do Lua rejeitam com erro críptico. Use VS Code ou Notepad++.

---

## Estrutura do repositório

```
wow-prestige-mod/
├── README.md                          este arquivo
├── lua_scripts/                       → D:\AzerothCore\server\lua_scripts\
│   ├── 01_prestige_config.lua         configuração central
│   ├── 02_prestige_core.lua           dados, cache, multiplicador de XP, aura
│   ├── 03_prestige_npc.lua            gossip, validação, confirmação
│   ├── 04_prestige_wipe.lua           execução destrutiva + recuperação
│   ├── 05_prestige_addon.lua          ponte servidor → addon
│   └── 06_prestige_maxlevel.lua       bônus de nível máximo (4 multiplicadores)
├── addon/
│   └── PrestigeUI/                    → <WoW>\Interface\AddOns\PrestigeUI\
│       ├── PrestigeUI.toc
│       └── PrestigeUI.lua
└── sql/                               executar manualmente, não copiar
    ├── prestige_characters.sql        schema base
    └── prestige_maxlevel_upgrade.sql  upgrade: coluna max_level_bonus
```

### Convenção de ordem de carga

O engine carrega arquivos `.lua` em **ordem alfabética do caminho completo**. Os prefixos numéricos são obrigatórios: `01_prestige_config.lua` define a tabela global `Prestige.Config`, que todos os outros leem no momento da carga.

Se novos módulos forem adicionados, mantenha o padrão `NN_prestige_*.lua` e **na mesma pasta** — colocar um arquivo em subpasta muda a posição dele na ordenação.

### O que não vai em `lua_scripts`

Apenas o que o engine executa. Arquivos `.sql`, anotações e versões desativadas ficam fora. Renomear para `.lua.old` funciona, mas mover para fora é mais seguro: um rename acidental de volta gera um bug difícil de rastrear.

---

## Instalação

### 1. Banco de dados

Ambos no banco **`acore_characters`**, nesta ordem:

```powershell
Get-Content sql/prestige_characters.sql       | mysql -u root -p acore_characters
Get-Content sql/prestige_maxlevel_upgrade.sql | mysql -u root -p acore_characters
```

Verificação:

```sql
SHOW TABLES FROM acore_characters LIKE 'character_prestige%';
-- esperado: character_prestige, character_prestige_log,
--           character_prestige_pending, character_prestige_skills

SHOW COLUMNS FROM acore_characters.character_prestige LIKE 'max_level_bonus';
-- esperado: 1 linha
```

### 2. Scripts Lua

Copiar `lua_scripts/*.lua` para `D:\AzerothCore\server\lua_scripts\`.

A pasta precisa conter também `extensions/` (com `ObjectVariables.ext`, `_Misc.ext` e `StackTracePlus/`), que vem da saída do build:

```powershell
Copy-Item "D:\AzerothCore\build\bin\RelWithDebInfo\lua_scripts\extensions" `
          "D:\AzerothCore\server\lua_scripts\" -Recurse
```

`StackTracePlus` não é opcional: sem ele, erros de Lua vêm sem número de linha.

### 3. NPC

Precisa de uma linha em `acore_world.creature_template` com o `entry` definido em `NPC_ENTRY` (padrão `190000`) e **`npcflag = 1`** (gossip). Sem esse flag, clicar no NPC não abre menu — sintoma indistinguível de script quebrado.

Nas versões atuais do core, o modelo visual fica em **`creature_template_model`**, tabela separada. Duplicar apenas a linha de `creature_template` produz este aviso no log de inicialização:

```
Creature (Entry: 190000) does not have any existing display id in creature_template_model.
```

Correção — copiar o modelo do mesmo NPC usado como base (confirmar nomes de coluna antes, variam entre versões):

```sql
INSERT INTO acore_world.creature_template_model
SELECT 190000, Idx, CreatureDisplayID, DisplayScale, Probability, VerifiedBuild
FROM acore_world.creature_template_model
WHERE CreatureID = <ENTRY_BASE>;
```

Spawn in-game: `.npc add 190000`

### 4. Addon (opcional, por jogador)

Copiar a pasta `addon/PrestigeUI/` para `<pasta do WoW>\Interface\AddOns\`. O nome da pasta precisa ser exatamente `PrestigeUI`, igual ao nome do `.toc`.

O addon é **puramente cosmético**. Sem ele o jogador recebe todos os bônus normalmente, apenas não vê o ícone.

### 5. Verificação

Subir o worldserver. O console deve mostrar uma linha do ALE informando **6 scripts carregados** e nenhum aviso de display id.

---

## Como o sistema funciona

### Fluxo do jogador

1. Jogador entre nível 30 e 79, **ou** no nível 80, procura o NPC de prestígio.
2. Menu mostra prestígio atual, multiplicador vigente e elegibilidade.
3. Ao escolher ascender, uma janela de confirmação lista o que será perdido, incluindo o aviso em maiúsculas sobre encantamentos.
4. Confirmado: itens vão por correio, progresso é apagado, contador incrementa, logout forçado.
5. No login seguinte, profissões são restauradas e o ícone reaparece com o novo tier.

### Curva de experiência

```
multiplicador = min(1 + contador × XP_STEP, XP_CAP)
```

Com os valores atuais (`XP_STEP = 0.4`, `MAX_PRESTIGE = 10`, `XP_CAP = 5.0`):

| Prestígio | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|
| XP | 1.4× | 1.8× | 2.2× | 2.6× | 3.0× | 3.4× | 3.8× | 4.2× | 4.6× | 5.0× |

**`XP_STEP` e `MAX_PRESTIGE` são calibrados em conjunto:** `1 + (10 × 0.4) = 5.0`, exatamente o teto. Alterar `MAX_PRESTIGE` sem recalcular `XP_STEP` faz os últimos prestígios virarem custo puro — o jogador perde tudo e o multiplicador não sobe.

### Bênção do Ápice

Concedida a quem prestigia estando no nível máximo (`SERVER_MAX_LEVEL = 80`). Estado **booleano**, não acumulativo — prestigiar duas vezes no 80 não dá 6×.

| Benefício | Multiplicador |
|---|---|
| Reputação | 3× |
| Honra de PvP | 3× |
| Pontos de profissão | 3× |
| Recursos coletados | 3× |

O NPC anuncia a existência da bênção **antes** da escolha, inclusive para quem ainda não chegou ao 80. Descobrir depois que esperar dois níveis dava um prêmio permanente seria motivo justo de reclamação.

### Tiers visuais (addon)

| Prestígio | Tier | Cor |
|---|---|---|
| 1–2 | Iniciado | bronze |
| 3–4 | Ascendente | prata |
| 5–6 | Exaltado | ouro |
| 7–9 | Venerado | azul |
| 10 | Eterno | roxo, com brilho pulsante |

A Bênção do Ápice aparece como estrela verde no canto do ícone.

---

## Referência de configuração

Tudo em `01_prestige_config.lua`, tabela `Prestige.Config`.

### Elegibilidade

| Chave | Padrão | Descrição |
|---|---|---|
| `MIN_LEVEL` | `30` | Nível mínimo da faixa normal |
| `MAX_LEVEL` | `79` | Nível máximo da faixa normal |
| `ALLOW_MAX_LEVEL` | `true` | Permite prestigiar no nível máximo. **Precisa ser `true`** ou a Bênção fica inalcançável |
| `SERVER_MAX_LEVEL` | `80` | Nível máximo do servidor |
| `MAX_PRESTIGE` | `10` | Teto de prestígios. `0` = sem limite (não recomendado) |

### Experiência

| Chave | Padrão | Descrição |
|---|---|---|
| `XP_STEP` | `0.4` | Incremento por prestígio |
| `XP_CAP` | `5.0` | Teto do multiplicador |

### Bênção do Ápice

| Chave | Padrão | Descrição |
|---|---|---|
| `MAXLEVEL_BONUS_ENABLED` | `true` | Liga/desliga o módulo inteiro |
| `MAXLEVEL_REP_MULT` | `3.0` | Reputação |
| `MAXLEVEL_SKILL_MULT` | `3.0` | Pontos de profissão |
| `MAXLEVEL_LOOT_MULT` | `3.0` | Recursos coletados |
| `MAXLEVEL_HONOR_MULT` | `3.0` | Honra de PvP |
| `MAXLEVEL_HONOR_POLL_MS` | `5000` | Intervalo do polling de honra |

### Itens e correio

| Chave | Padrão | Descrição |
|---|---|---|
| `MAIL_SUBJECT` | — | Assunto das cartas |
| `MAIL_BODY` | — | Corpo das cartas |
| `MAIL_DELAY` | `0` | Segundos até a entrega |
| `MAX_ITEM_STACKS` | `60` | Teto de pilhas. Acima disso, aborta e pede para limpar a mochila |
| `INCLUDE_BANK` | `false` | Banco não é esvaziado — já é armazenamento seguro |

### Reset

| Chave | Padrão | Descrição |
|---|---|---|
| `RESET_TALENTS` | `true` | Zera talentos |
| `RESET_ACHIEVEMENTS` | `true` | Zera conquistas |
| `RESET_SPELLS` | `true` | Zera magias. **Requer o snapshot de profissões** (ver abaixo) |
| `RESET_LEVEL_SKILLS` | `true` | **[ajuste local]** Devolve para 1 as perícias que escalam com o nível: armas, escolas de magia e Defesa. Ver abaixo |

### NPC e visual

| Chave | Padrão | Descrição |
|---|---|---|
| `NPC_ENTRY` | `190000` | Entry em `creature_template` |
| `GOSSIP_TEXT_ID` | `100` | `npc_text.ID` do cabeçalho |
| `AURA_SPELL_ID` | `0` | Aura cosmética. `0` = desligada. Ver limitações |
| `DEBUG` | `true` | Logging verboso |

### Tabelas auxiliares

| Nome | Conteúdo |
|---|---|
| `Prestige.PROFESSION_SKILLS` | IDs de skill preservados no reset (15 profissões + montaria) |
| `Prestige.PROTECTED_ITEMS` | Entries que não vão para o correio (moedas especiais) |
| `Prestige.RESOURCE_BLACKLIST` | Entries que não recebem o multiplicador de coleta |

---

## Arquitetura por módulo

### `01_prestige_config.lua`

Apenas dados. Nenhuma lógica, nenhum hook. Separar config de lógica permite ajustar balanceamento e dar `.reload eluna` sem risco de quebrar comportamento.

### `02_prestige_core.lua`

Camada de dados e o multiplicador de XP.

Expõe: `Prestige.LoadCount`, `Prestige.SaveCount`, `Prestige.GetCount`, `Prestige.GetMultiplier`, `Prestige.ApplyAura`, `Prestige.Log`.

Mantém um cache em memória populado no login e descartado no logout. **O cache nunca é a fonte da verdade** — ver [Multistate](#multistate-e-o-cache).

### `03_prestige_npc.lua`

Gossip e validação. Expõe `Prestige.CanPrestige(player) → bool, motivo`.

Valida: faixa de nível, teto de prestígio, combate, trade aberto, battleground/arena, instância com grupo, morte, caixa de correio cheia, número de pilhas de itens.

**A validação roda três vezes**: ao desenhar o menu, ao clicar em "quero ascender", e ao confirmar. O jogador pode entrar em combate entre um clique e outro; estado capturado quando o menu foi montado não vale nada no momento da execução.

### `04_prestige_wipe.lua`

A parte destrutiva. Expõe `Prestige.Execute`, `Prestige.Resume`, `Prestige.DryRun`, `Prestige.CountItemStacks`.

Ordem obrigatória das operações:

```
1. validar                    (feito no NPC)
2. gravar WAL                 ← ponto sem volta registrado no banco
3. snapshot de profissões
4. enviar itens por correio
5. remover itens do inventário
6. resetar nível/quests/conquistas, e as perícias de nível depois do nível
7. incrementar contador
8. conceder Bênção (se oldLevel >= SERVER_MAX_LEVEL)
9. WAL → DONE, logout forçado
10. próximo login: restaurar profissões, limpar WAL
```

Dentro do loop de correio, a carta é enviada **antes** de o item ser removido. Se a carta falhar, o item continua com o jogador. A ordem inversa perderia o item.

Se qualquer envio falhar, o wipe é abortado e o personagem fica intacto. Melhor um jogador com itens duplicados no correio do que um personagem zerado ainda segurando equipamento.

### `05_prestige_addon.lua`

Ponte servidor → cliente. Expõe `Prestige.SendUI(player)`.

Protocolo: `P:<contador>:<multiplicador>:<bônus>` — por exemplo `P:7:3.8:1`. O campo de bônus é opcional no parser do addon, então clientes com versão anterior continuam funcionando.

Envio com 5 segundos de atraso após o login, porque o addon precisa ter terminado de carregar e registrado `CHAT_MSG_ADDON`. Enviar no instante do login é enviar para o vazio.

### `06_prestige_maxlevel.lua`

Os quatro multiplicadores da Bênção. Expõe `Prestige.HasMaxLevelBonus`, `Prestige.GrantMaxLevelBonus`.

Cache próprio de flag, separado do cache de contador do núcleo, mesma regra.

---

## Hooks utilizados

Todos via `RegisterPlayerEvent(id, fn)`, exceto os de gossip.

| ID | Evento | Assinatura | Uso | Retorna? |
|---|---|---|---|---|
| 3 | `ON_LOGIN` | `(event, player)` | Cache, aura, retomada de WAL, envio ao addon | não |
| 4 | `ON_LOGOUT` | `(event, player)` | Limpeza de cache | não |
| 12 | `ON_GIVE_XP` | `(event, player, amount, victim, source)` | **Multiplicador de XP** | **sim** |
| 15 | `ON_REPUTATION_CHANGE` | `(event, player, factionId, standing, incremental)` | Multiplicador de reputação | **sim** |
| 32 | `ON_LOOT_ITEM` | `(event, player, item, count)` | Multiplicador de coleta | não |
| 61 | `ON_BEFORE_UPDATE_SKILL` | `(event, player, skill_id, value, max, step)` | Multiplicador de profissão | **sim** |

Gossip: `RegisterCreatureGossipEvent(NPC_ENTRY, 1, fn)` para hello, `(…, 2, fn)` para select.

### Armadilhas por hook

**Hook 15 — reputação.** Retornar `-1` faz o core **bloquear o ganho inteiro**. Qualquer cálculo que resulte em zero ou negativo transforma o bônus em "esse jogador nunca mais ganha reputação". O código força mínimo de 1.

Multiplicar só ocorre quando `incremental` é verdadeiro. Quando falso, `standing` é o valor absoluto sendo definido, e triplicar isso levaria o jogador a exaltado de uma vez. Perdas de reputação (`standing <= 0`) passam intactas.

**Hook 61 — profissão.** Multiplica o **passo**, não o valor. Multiplicar o valor levaria um minerador de 150 para 450 num único minério. Resultado é limitado por `max`.

**Hook 32 — loot.** Não aceita retorno. O bônus é implementado adicionando cópias com `AddItem`. Isso é seguro contra loop infinito porque `AddItem` dispara `ON_STORE_NEW_ITEM` (53), nunca `ON_LOOT_ITEM` (32). **Se algum dia registrar algo no 53, revisar este ponto.**

Filtro por `item:GetClass() == 7` (Trade Goods), que cobre minério, erva, couro, pano, pó de encantamento e peixe.

**Hook 12 — XP.** Há guarda contra estouro: XP é `uint32` no core, e multiplicador alto sobre valor já grande gera comportamento indefinido.

---

## Decisões de design e por quê

### Multistate e o cache

O ALE pode rodar em modo **multistate**, onde cada mapa tem seu próprio estado Lua isolado. `IsCompatibilityMode()` retorna `true` em compatibilidade e `false` em multistate.

Em multistate, uma tabela Lua global não é compartilhada entre mapas: o jogador que troca de continente troca de estado, e dados em memória ficam para trás. Por isso **o banco é sempre a fonte da verdade**, e todo cache é populado no login e descartado no logout.

Verificar o modo ativo em `mod_ale.conf` antes de assumir qualquer coisa sobre estado compartilhado.

### Write-ahead log

`character_prestige_pending` guarda o estágio da operação. Três estados:

| Estágio | Significado | Ação na retomada |
|---|---|---|
| `MAILING` | Cartas em andamento | **Congela e chama GM** |
| `WIPING` | Cartas já saíram | Termina automaticamente |
| `DONE` | Falta restaurar profissões | Restaura no próximo login |

`MAILING` não é retomado automaticamente de propósito. O estado é ambíguo — parte dos itens pode ter ido — e agir no escuro poderia **duplicar** itens. Duplicação é pior que perda: uma some da economia, a outra a destrói.

O WAL não é apagado ao fim de `Execute`. Ele precisa sobreviver até o login seguinte para disparar a restauração de profissões, que só pode acontecer depois de as flags `at_login` rodarem.

### Profissões: snapshot e restauração

A forma idiomática de zerar magias e talentos no core são as flags `at_login` (`RESET_SPELLS = 0x08`, `RESET_TALENTS = 0x10`), executadas no próximo carregamento do personagem, fora de qualquer caminho quente.

O problema: `AT_LOGIN_RESET_SPELLS` também apaga as magias de profissão. Daí `character_prestige_skills` — valor e máximo de cada profissão são salvos antes do wipe e restaurados no login seguinte.

Sem `RESET_SPELLS`, um personagem nível 1 sai por aí com magias de nível 80. Com ele, as profissões precisam do snapshot. Não há terceira opção.

### Perícias de nível: o core não as baixa

**[ajuste local]** `RESET_SPELLS` apaga as *magias*, não os valores das
*perícias*. São coisas diferentes, e sem tratar a segunda o personagem fica
nível 1 com Fogo em 400.

O motivo está no core, e vale conhecer porque não é intuitivo:

- Quando o nível muda, `UpdateSkillsForLevel` faz
  `MAKE_SKILL_VALUE(val, maxSkill)` — atualiza o **máximo** e mantém o
  **valor** (`PlayerUpdates.cpp`). No carregamento, `_LoadSkills` faz o mesmo:
  `max = GetMaxSkillValueForLevel()`, e não toca no valor.
- `AT_LOGIN_RESET_SPELLS` chama `LearnDefaultSkills`, que por perícia faz
  `if (HasSkill(skillId)) continue` — perícia que o personagem já tem é pulada,
  com o valor antigo intacto.

Nada disso é bug: o core nunca foi feito para nível **caindo**. Então o reset é
explícito, em `Prestige.LEVEL_SKILLS`, depois do `SetLevel(1)` — o `SetLevel` do
ALE chama `Player::GiveLevel`, que já acerta o máximo, então nesse ponto
`GetMaxSkillValue` devolve 5 e não 400.

A tabela lista o que **resetar**, não o que preservar. Se faltar uma entrada,
uma perícia fica alta — chato e visível. Se fosse lista de exceções e faltasse
uma, apagaria idioma (300/300) ou profissão — dano. Idiomas e proficiências de
armadura (1/1) ficam de fora por não estarem na lista.

`HasSkill` antes de `SetSkill` não é otimização: `SetSkill` numa perícia que o
personagem não tem **a ensina**, e um mago sairia sabendo Espadas.

Profissões seguem preservadas pelo snapshot — as duas tabelas não se cruzam, e
um teste confere isso.

### Correio: itens por entry, não por instância

`SendMail(subject, text, receiverGUIDLow, senderGUIDLow, stationary, delay, money, cod, entry, amount)` recebe um **entry**, não uma instância de item. O servidor cria um item novo na carta.

**Consequência aceita: encantamentos, gemas, durabilidade e propriedades aleatórias se perdem.** Isso está documentado em maiúsculas na tela de confirmação do jogador. Não é bug.

Preservar as instâncias exigiria inserir direto em `mail` + `mail_items` e repontuar `item_instance.owner_guid`. O core gera IDs de correio a partir de um contador em memória inicializado no boot; qualquer linha inserida com `MAX(id)+1` colide com o próximo ID gerado pelo core e **corrompe correio de outros jogadores**. Rejeitado por risco.

Consequência secundária: a API aceita um par entry/amount por chamada, então cada pilha vira uma carta. `MAX_ITEM_STACKS = 60` corta antes que isso vire absurdo.

### O bônus de XP não é uma aura

O multiplicador vive no retorno do hook 12. A aura, quando configurada, é **puramente cosmética**.

Acoplar o efeito ao ícone significaria que qualquer dispel, troca de mapa ou bug de aura roubaria o bônus que o jogador pagou caro para ter. Se a aura falhar em aplicar, o jogador perde o ícone, não o benefício.

Mesma lógica para o addon: se não estiver instalado, todos os bônus continuam funcionando.

### Ícone: por que addon em vez de spell customizada

O cliente 3.3.5a só desenha ícone de buff para magias que ele já conhece do próprio `Spell.dbc`. Uma magia customizada em `spell_dbc` é conhecida apenas pelo servidor: o cliente recebe o aura update, não encontra a entrada, e não desenha nada.

As três opções eram:

| Abordagem | Custo | Resultado |
|---|---|---|
| Reaproveitar magia existente | baixo | ícone real, **nome errado no tooltip** |
| Patch de cliente (MPQ com DBC) | alto | correto, exige todo jogador baixar |
| Addon próprio | médio | controle total, exige instalação |

Escolhido o addon: o dado já estava no servidor e `SendAddonMessage` já existia na API. Resultado é um frame arrastável com tooltip próprio, sem mentira de nome.

A opção de aura permanece disponível via `AURA_SPELL_ID` para quem não quiser instalar addon. Deixada em `0` (desligada) por padrão.

### Honra: polling, e por quê é feio

**Não existe hook de ganho de honra no ALE.** As alternativas eram:

- **Hook 6 (`ON_KILL_PLAYER`) e estimar a honra.** Perderia honra de objetivo de battleground, de quest e de bônus de fim de partida.
- **Comparar o total periodicamente e creditar a diferença.** Pega todas as fontes.

Escolhida a segunda. O preço é visível: o jogador vê a honra chegar em duas etapas — o valor base, e alguns segundos depois o bônus.

O baseline é atualizado para o total **depois** de creditar o bônus. Sem isso, o ciclo seguinte veria o próprio bônus como ganho novo e multiplicaria de novo, indefinidamente.

**Se o ALE ganhar um hook de honra, remover `PollHonor` inteiro.** Está isolado justamente para isso.

### Bênção não acumula

Estado booleano. Se um dia for desejável acumular, a mudança é trocar a coluna `max_level_bonus` de flag para contador — mas avaliar antes o que 9× de coleta faz com a economia do servidor.

---

## Limitações conhecidas

| Limitação | Impacto | Contorno |
|---|---|---|
| Encantamentos e gemas se perdem no correio | Alto | Nenhum viável — ver decisão acima |
| Uma carta por pilha de item | Médio | `MAX_ITEM_STACKS` limita o volume |
| Bônus de honra chega com atraso visível | Baixo | Reduzir `MAXLEVEL_HONOR_POLL_MS` |
| Ícone só atualiza no relogin | Baixo | Irrelevante: o mod força logout ao prestigiar |
| WAL travado em `MAILING` exige GM | Baixo | Deliberado — evita duplicação |
| Banco pessoal não é esvaziado | Nenhum | Deliberado |
| Multiplicador de coleta afeta todo Trade Goods | Médio | `RESOURCE_BLACKLIST` |

### Sobre `RESOURCE_BLACKLIST`

Está vazia. Se algum material funcionar como moeda no servidor, adicionar **antes** que o mercado descubra o multiplicador. Materiais de economia sensível são o caso de uso.

---

## Superfície de API não verificada

Métodos confirmados na documentação do ALE: `GetItemByPos`, `RemoveItem`, `AddItem`, `ResetAchievements`, `RemoveQuest`, `GetQuestSlotQuestId`, `ResetTalents`, `SetAtLoginFlag`, `SetSkill`, `HasSkill`, `GetSkillValue`, `GetMaxSkillValue`, `GetMailCount`, `GetTrader`, `SetPlayerLock`, `SaveToDB`, `LogoutPlayer`, `SendMail`, `CreateLuaEvent`, `GetPlayerByGUID`, `GetPlayersInWorld`, `Item:GetClass`, `Item:GetSubClass`, `CharDBQuery`, `CharDBExecute`, `PrintInfo`, `PrintError`.

**Não confirmados — envolvidos em `pcall` no código:**

| Item | Onde | Risco |
|---|---|---|
| `ADDON_CHANNEL = 6` | `05_prestige_addon.lua` | **Suspeito nº 1.** Enum de canais fica do lado C++, fora da doc. Se o addon não receber nada, testar `7` e `0` |
| `Unit:AddAura` + `Aura:SetDuration` | `02_prestige_core.lua` | Só afeta a aura cosmética opcional |
| `SetSkill` (ordem dos argumentos) | `04_prestige_wipe.lua` | Afeta restauração de profissões — **validar no teste** |
| `GetMap():IsDungeon()` | `03_prestige_npc.lua` | Falha = validação de instância não funciona |
| `GetHonorPoints` / `ModifyHonorPoints` | `06_prestige_maxlevel.lua` | Falha = bônus de honra silenciosamente inativo |
| Colunas de `creature_template_model` | SQL de instalação | Variam entre versões do core |

Com `DEBUG = true`, falhas em `pcall` são impressas no log do worldserver.

---

## Protocolo de teste

**Sempre com personagem descartável. Nunca com personagem que importa.**

### Antes de qualquer teste destrutivo

```powershell
mysqldump -u root -p acore_characters > backups/acore_characters-pre-teste.sql
```

Conferir que o arquivo tem alguns MB. Um dump que falhou também termina sem reclamar — o erro fica na primeira linha do arquivo.

### Sequência

1. **Carga.** Subir o worldserver, confirmar 6 scripts Lua carregados, sem aviso de display id.
2. **UI isolada.** In-game: `/prestige test 1`, `test 5`, `test 10`. Verifica os cinco tiers e o pulso do tier final sem envolver o servidor. Chamadas repetidas alternam a estrela da Bênção.
3. **Menu.** `.npc add 190000`, clicar. Confirmar prestígio atual, elegibilidade correta e o aviso da Bênção.
4. **Dry run.** `Prestige.DryRun(player)` — lista item por item o que seria enviado, sem tocar em nada. **Testar com bolsa dentro de bolsa** e com bolsas cheias.
5. **Item encantado.** Confirmar visualmente que o encantamento se perde. É comportamento esperado, não bug — mas precisa ser visto para não virar surpresa em produção.
6. **Profissão em 450.** Prestigiar e confirmar que volta após o relogin. Valida a assinatura de `SetSkill`.
6b. **Perícia de arma e de magia.** Antes de prestigiar, anotar Fogo (ou a escola
   da classe) e a arma em uso na aba de perícias. Depois do relogin, as duas
   precisam estar em **1/5**, e as profissões nos valores antigos. É o mesmo
   `SetSkill` dos dois lados: se um funcionar e o outro não, o problema é a
   tabela, não a API.
7. **Crash no meio.** `kill -9` no worldserver durante o envio de cartas. Confirmar que o WAL detecta na volta e congela em `MAILING`.
8. **Bênção.** Prestigiar no nível 80. Verificar cada multiplicador separadamente:
   - reputação: matar mob com facção associada, comparar ganho
   - profissão: minerar/colher, observar `+3` no log com `DEBUG = true`
   - coleta: lootear pano, conferir a quantidade extra
   - honra: matar jogador, esperar o ciclo de polling
9. **Não acumula.** Prestigiar duas vezes no 80, confirmar que os multiplicadores continuam 3×.

O passo 7 é o que separa um mod de servidor privado de um mod que pode ficar rodando sem supervisão.

---

## Diagnóstico

### ALE não carrega scripts

Se o log de inicialização não menciona ALE, Eluna ou carregamento de scripts — nem sequer "0 scripts" — o módulo está desativado. Verificar a opção de habilitar em `mod_ale.conf`. O arquivo aparecer na lista de configs carregados significa apenas que existe, não que está ativo.

Se carrega 0 scripts: caminho errado. Conferir a opção de script path no mesmo arquivo e se os arquivos não estão como `.lua.txt`.

Se carrega mas reclama de sintaxe: o erro traz arquivo e linha (graças ao `StackTracePlus`).

### NPC não abre menu

Ordem de verificação: `npcflag = 1` na linha de `creature_template`; `NPC_ENTRY` no config bate com o entry real; scripts carregados; erro no log.

### Addon não recebe dados

1. `/prestige test` funciona? Se não, o addon não carregou — verificar nome da pasta e a lista de AddOns na tela de personagens.
2. Se funciona, ativar `DEBUG = true` em `PrestigeUI.lua` e `/reload`. Todas as mensagens de addon passam a ser impressas no chat.
3. Se nenhuma linha com `PRESTIGE` aparece após relogar e esperar 5 segundos, a mensagem não está saindo do servidor. Trocar `ADDON_CHANNEL` em `05_prestige_addon.lua` para `7`, depois `0`.

### Ícone vira ponto de interrogação

Caminho de textura inválido na tabela `TIERS`. `Interface\\Icons\\INV_Misc_QuestionMark` sempre existe e serve para confirmar que o resto funciona. Note as barras duplas: em Lua, `\` é escape.

### Personagem travado em prestígio

```sql
SELECT * FROM acore_characters.character_prestige_pending WHERE guid = <GUID>;
SELECT * FROM acore_characters.character_prestige_log WHERE guid = <GUID> ORDER BY at;
```

`character_prestige_log` registra cada etapa. `STUCK` com detalhe `interrompido em MAILING` significa intervenção manual: conferir o correio do jogador, comparar com o inventário, decidir caso a caso. **Não limpar o WAL sem antes auditar o log.**

---

## Roadmap

Itens desejáveis, nenhum bloqueante:

- **Comando de GM** para conceder e revogar prestígio manualmente. Será necessário no primeiro ticket. Ao alterar o contador, chamar `Prestige.SendUI(player)` para atualizar o ícone na hora.
- **Anúncio global** via `SendWorldMessage` quando alguém ascende. Engajamento social de custo zero.
- **Título** por tier via `SetKnownTitle` — visível para outros jogadores sem exigir addon.
- **Prestígio por conta** em vez de por personagem. A coluna `account` já existe na tabela.
- **Preservação de instâncias de item**, caso apareça uma forma segura de gerar IDs de correio.
- **Substituir o polling de honra** se o ALE ganhar hook de honra.
- ~~**Auditoria de weapon skills e proficiências** após `RESET_SPELLS`~~ — feito:
  ver [Perícias de nível](#perícias-de-nível-o-core-não-as-baixa). Proficiências
  de armadura são `1/1` e não precisam de nada.
