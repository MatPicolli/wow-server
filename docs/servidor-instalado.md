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
> `.bat`, o set de herança de 48 peças — mora neste repositório. Está tudo na
> máquina do Mateus. Se um dia isso precisar sobreviver a uma formatação, o
> caminho é trazer esses arquivos para cá, não recriá-los a partir deste texto.

Instruções para o agente que mexe naquela instalação. Leia antes de mexer em qualquer coisa.

Este é um servidor **pessoal, single-player**, com playerbots fazendo o papel de população.
Não há outros jogadores reais. Isso muda as prioridades: estabilidade e conveniência valem
mais que balanceamento competitivo ou proteção contra abuso.

---

## Identidade do servidor

| Item | Valor |
|---|---|
| Core | AzerothCore, branch **Playerbot** |
| Revisão | `ceeb3116e` (2026-07-24) |
| Plataforma | Windows, Visual Studio 17 2022, `RelWithDebInfo`, `MODULES=static` |
| Cliente | WotLK 3.3.5a, roda **na mesma máquina** que o servidor |
| Hardware | 6–8 núcleos, 32 GB |
| Banco | MySQL 8.4 — `acore_auth`, `acore_world`, `acore_characters` |

Credenciais do banco: leia de `server/configs/worldserver.conf` (`LoginDatabaseInfo` etc.).
Não replique a senha em documentação nova.

## Layout

```
D:\AzerothCore\
├── source/          fonte do core + source/modules/
├── build/           saída do CMake/VS (CMakeCache.txt tem o gerador)
├── server/          instalação: worldserver.exe, configs/, Data/dbc/, lua_scripts/, logs/
├── backups/         dumps .sql e backups de DBC
├── PrestigeDraft/   SQL, arquivos de cliente e .bat do mod de prestígio
└── *.bat *.sql *.md scripts utilitários e guias na raiz
```

## Módulos instalados

`mod-account-achievements`, `mod-ah-bot`, `mod-ale`, `mod-aoe-loot`, `mod-autobalance`,
`mod-guildhouse`, `mod-instance-reset`, `mod-playerbots`, `mod-solo-lfg`, `mod-solocraft`.

**`mod-ale`** é o motor Lua (Eluna renomeado). Todo script em `server/lua_scripts/` depende dele.

---

## Armadilhas conhecidas — leia isto antes de escrever SQL

### 1. O banco do mundo está num schema MAIS ANTIGO que o `source`

Esta é a pegadinha que mais custou tempo. Exemplo concreto:

| Tabela | `source/data/sql/base/` | Banco real |
|---|---|---|
| `creature` | `id1`, `id2`, `id3` | **`id`** |

**Nunca valide SQL de terceiros contra `source/data/sql/base/`.** Valide contra o banco vivo.
O jeito barato: pegue o dump mais recente em `backups/` e extraia os `CREATE TABLE` de lá.

```bash
awk '/^CREATE TABLE `creature` \(/,/^\) ENGINE/' backups/<dump>.sql
```

Sintomas típicos de ignorar isso: `ERROR 1054 Unknown column 'X' in 'field list'`.

Já corrigido por causa disso: `chromie_spawn.sql` (usava `id1`) e `cleanup_modulos_removidos.sql`.

### 2. `creature_template` perdeu 7 colunas nesta versão

`scale`, `trainer_type`, `trainer_spell`, `trainer_class`, `trainer_race`,
`mechanic_immune_mask`, `spell_school_immune_mask` não existem mais.
SQL de mods antigos vai quebrar nelas.

### 3. SQL de terceiros raramente é idempotente

Padrão observado em três mods diferentes: `CREATE TABLE IF NOT EXISTS` seguido de
`INSERT INTO` puro. Roda uma vez, quebra na segunda com `ERROR 1062 Duplicate entry`.

**Regra da casa: todo SQL neste repositório deve poder rodar duas vezes.** Use
`DELETE` antes de `INSERT`, ou `INSERT IGNORE` / `REPLACE INTO`. Para colunas, use o
padrão do `PrestigeDraft/SQL/Acore_characters/prestige_corrigido.sql`: um procedure que
consulta `information_schema` e só adiciona o que falta (MySQL 8 não tem
`ADD COLUMN IF NOT EXISTS`).

### 4. Marque toda edição em arquivo de terceiro

Comentário `-- [ajuste local]` ou `-- [custom]`, explicando **o erro que aquilo evita**.
Quando o mod for atualizado, esses comentários são o que permite reaplicar as correções.

### 5. Adicionar um módulo exige re-rodar o CMake

Não basta buildar. É preciso **Configure + Generate** no CMake para ele enxergar a pasta
nova em `source/modules/`, e depois buildar o projeto **INSTALL** (não o `worldserver`
direto — o INSTALL é o que copia binários e `.conf.dist` para `server/`).

Sintoma de esquecer: o build passa, o servidor sobe, e o módulo simplesmente não existe.
Confirme em `server/logs/Server.log`.

### 6. Armadilhas de `.bat` no Windows

Duas que já morderam:

- `title Foo & Bar` — o `&` separa comandos. Escape: `^&`.
- `for %%F in (` com a lista quebrada em várias linhas **não funciona**. O laço não é
  reconhecido, `%%F` sai literal como `%F`. Use uma linha só, ou chame uma sub-rotina
  por arquivo (padrão usado em `PrestigeDraft/aplicar_sql.bat`).
- `mysqldump` no MySQL 8 precisa de `--no-tablespaces`, senão falha por falta da
  permissão `PROCESS`.

---

## Convenções dos scripts `.bat`

Todo `.bat` que toca o banco segue esta forma:

1. Avisa para parar o worldserver e dá `pause`.
2. Testa `mysql` no PATH e a conexão.
3. **Faz `mysqldump` antes de qualquer escrita e aborta se o backup falhar ou vier pequeno.**
4. Aplica cada arquivo via sub-rotina, parando no primeiro erro.
5. Termina com consultas de verificação que provam que deu certo.

Se você criar um `.bat` novo, siga isso. O backup que aborta na falha é o que torna
seguro rodar coisas destrutivas.

---

## Customizações locais ativas

### IDs reservados — não reutilize

| Faixa | Uso |
|---|---|
| `item_template` 700000–700047 | set de herança completo (48 peças) |
| `creature_template` 2069426 | Chromie, a NPC de prestígio |
| `creature` guid 5300512 / 5300513 | spawns da Chromie (Ironforge / Orgrimmar) |
| `gameobject_template` 500030 | vendedor de guild house (precisa `.npc add`) |

### Prestige & Draft Mode (Lua, via mod-ale)

- `CONFIG.MAX_LEVEL = 30` em `prestige_and_spell_choice_config.lua`. Apesar do nome, é o
  nível **mínimo** para prestigiar. O `total_expected_drafts` do draft usa o nível real
  do personagem, não esta constante — baixá-la não quebra a matemática do draft.
- Em Draft Mode o personagem **não tem talentos**: `goodbye_talentpoints_draft.lua` zera
  os pontos no login e a cada level. Não está documentado no README do mod.

### Bônus permanentes de prestígio — `server/lua_scripts/prestige_bonuses.lua`

Multiplicadores lidos de `acore_characters.prestige_stats`, com cache em memória por GUID.

| Bônus | Regra | Hook |
|---|---|---|
| XP de mobs e quests | `min(1 + prestige_level, 10)` | 12 |
| Reputação 3x | só com `maxlevel_prestige = 1` | 15 |
| Profissão 3x | idem | **62** |
| Honra 3x | idem, só abate de PvP | 6 |
| Materiais em dobro | idem | 32 |

**Não são auras, e isso é deliberado.** Aura é removível e some em vários casos; o pedido
era "permanente, nem na morte". Lido do banco, só some se o personagem for deletado —
tratado no hook 2.

**Cuidado com o hook 61.** Ele parece o caminho óbvio para multiplicar profissão, mas o
`value` dele é o valor **absoluto** da skill, não o ganho (`PlayerUpdates.cpp:722` e `:938`).
Multiplicar ali saltaria Mineração de 150 para 450. Por isso o bônus usa o hook 62, que
dispara depois do skill-up.

**Honra é parcial por limitação do ALE.** Não existe hook de honra; o contorno lê os
pontos, espera 250 ms e soma o dobro do ganho. Pega abate de jogador, **não** pega honra
de objetivo nem de vitória de battleground.

### Set de herança completo

O WotLK 3.3.5a só tem herança de ombro e peito (20% de XP). Foram gerados 8 slots novos
(elmo, amuleto, braçadeiras, manoplas, cinto, grevas, botas, manto) × 6 variantes de
armadura/papel = 48 itens, cada um com o feitiço **57353** (+10% XP). Total: **100%**.

Isso funciona porque auras de **itens diferentes** com o mesmo feitiço empilham —
`SpellAuras.cpp:1963`, com comentário explícito no core. É o mesmo motivo de ombro + peito
darem 20% hoje.

Os stats escalam de verdade via `ScalingStatDistribution` (reusada da ombreira equivalente)
e `ScalingStatValue`, cujo bitmask está em `DBCStructure.h`:

- orçamento de stats: `0x1` ombro, `0x8` peito
- armadura tier ombro: `0x20` tecido, `0x40` couro, `0x80` malha, `0x100` placa
- armadura tier peito: `0x100000` … `0x800000`; capa `0x80000`

Fórmula de entry: `700000 + (índiceDoSlot * 6) + índiceDaVariante`. A tabela
`CLASS_VARIANT` em `prestige_chromie.lua` mapeia classe → variante e foi deixada isolada
justamente para ajuste fácil.

Ressalva registrada: o DBC só tem dois níveis de orçamento, então braçadeiras e cinto
usam o de ombro e ficam levemente generosos. Aproximação deliberada.

### Outros ajustes

- `NoResetTalentsCost = 1` — reset de talentos grátis.
- `rbac_linked_permissions (195, 716)` — libera `.reset talents` para contas sem GM.
  A conta pessoal usa nível 0; a de administração tem nível 3.
- Playerbots: 1200 bots, `MapUpdate.Threads = 3`, `ProcessPriority = 0` (Normal — o
  cliente roda na mesma máquina e prioridade alta causava travadinha),
  `BotActiveAloneForceWhenInZone = 0` (cortava o pico nas capitais).

---

## Ao adicionar um mod novo

1. Ler o README **e** listar os arquivos do repositório — mods costumam ter scripts não
   documentados (foi o caso do `goodbye_talentpoints_draft.lua`).
2. Conferir se é C++ (precisa rebuild) ou Lua (só precisa do mod-ale).
3. **Validar todo SQL contra o dump vivo antes de rodar**, não contra o `source`.
   Vale extrair as colunas de todas as tabelas do dump uma vez e conferir os `INSERT`
   de uma vez só, em vez de descobrir um erro por execução.
4. Tornar o SQL idempotente.
5. Fazer backup de qualquer `.dbc` antes de sobrescrever (`backups/dbc-original-*`).
6. Checar conflito com o que já existe. Precedente: `mod-learn-spells` dava todas as
   magias automaticamente e teria anulado o Draft Mode inteiro.

## Verificação — não confie, meça

Este repositório tem histórico de suposições erradas. Alguns hábitos que pegaram erro real:

- Ler o **log**, não só o `.conf`. `RandomBotAccountCount = 0` parecia "bots desligados";
  o `Playerbots.log` mostrava 100 contas criadas e funcionando — `0` significa automático.
- Conferir a assinatura de hooks e o call site no core antes de usar.
- Validar Lua com `lupa` (`pip install lupa --break-system-packages`) — compila sem executar.
- Conferir contagem de colunas e balanceamento de aspas em SQL gerado por script.

## Documentos relacionados

| Arquivo | Assunto |
|---|---|
| `AHBOT-PASSOS.md` | configuração do AH Bot |
| `PRESTIGE-PASSOS.md` | instalação do Prestige & Draft Mode |
| `ahbot_setup.bat` | cria personagem do bot e ajusta o conf |
| `cleanup_modulos_removidos.bat` | remove restos de módulos desinstalados |
| `PrestigeDraft/aplicar_sql.bat` | aplica todo o SQL do prestígio |
| `reset_talents_para_jogadores.sql` | libera `.reset talents` via RBAC |
