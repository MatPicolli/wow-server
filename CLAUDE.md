# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repository is

Tooling to build and run a personal **AzerothCore 3.3.5a (WotLK, client build
12340)** World of Warcraft server **on Windows**, compiled from source. It
contains no server code — AzerothCore is cloned into `C:\AzerothCore\source`
(configurable) and built there.

Two layers:

- `scripts/` — PowerShell scripts that do all the actual work.
- `gui/` — a WPF front end (.NET 10) that **drives those scripts**. It does not
  reimplement any of them.

**The user is Brazilian. All user-facing text — script output, GUI labels,
docs, error messages — is in Portuguese.** Code identifiers, comments and
commit messages are in English, except in the PowerShell scripts where comments
are Portuguese without accents (they are read in consoles with varying code
pages).

## Commands

Everything below runs from the repository root unless noted.

### Server lifecycle (PowerShell, Windows)

```powershell
Copy-Item config\settings.example.psd1 config\settings.psd1   # first time; then edit ClientDir
.\scripts\00-check-prereqs.ps1        # diagnose in seconds instead of failing 30 min into a build
.\scripts\setup-all.ps1               # steps 2-7 chained; -From N resumes; -SkipMmaps skips the multi-hour pass
.\scripts\start-server.ps1            # opens authserver + worldserver
.\scripts\stop-server.ps1             # clean shutdown; -Force kills
.\scripts\rebuild.ps1                 # build -> deploy -> configure, after adding/removing modules
.\scripts\start-mysql.ps1 -Automatic  # start the MySQL service (needs Administrator)
.\scripts\backup-db.ps1               # safe to run against a live server
.\scripts\repair-settings.ps1         # recover a settings.psd1 with duplicate keys
.\scripts\switch-core.ps1 -Playerbots # swap the core for a fork; preview, then -Apply
.\scripts\remove-module.ps1 -Name mod-eluna   # preview, then -Apply
.\scripts\reset-server.ps1           # wipe everything rebuildable, keep the extraction
.\scripts\gm-heirlooms.ps1 -Character X     # mails every heirloom the DB has
.\scripts\gm-items.ps1 -Query "SELECT ..."   # read-only; the GUI item browser calls it
```

Numbered scripts `00`–`08` are the install pipeline and are individually
runnable and idempotent. `01-install-prereqs.ps1` needs Administrator.

Tuning scripts preview by default and require `-Apply` to write:

```powershell
.\scripts\tune-professions.ps1 -Mining 3 -Herbalism 3      # gathering yield (MinCount/MaxCount)
.\scripts\tune-drop-chance.ps1 -QuestItems 3               # drop chance (Chance column)
.\scripts\tune-professions.ps1 -Reset -Apply               # restore originals
.\scripts\tune-config.ps1 -Setting Rate.XP.Kill=3,Rate.MoveSpeed.Player=1.5   # worldserver.conf
```

### GUI

```powershell
cd gui
dotnet run --project WowServer.Core.Tests    # tests — run these, they work on any platform
dotnet run --project WowServer.Gui           # Windows only (WPF)
.\build.ps1 -Test -Publish                   # publish a single-file exe to gui\publish
```

There is no test framework. `WowServer.Core.Tests` is a plain console app with a
local `Check(name, condition)` helper; it prints results and exits non-zero on
failure. Add cases by appending to `Program.cs`.

## Architecture

### The scripts are the source of truth

The GUI shells out to `powershell.exe -File scripts\<name>.ps1` and streams the
output into console panes. Behaviour that took real debugging to get right —
OpenSSL 3.x detection, both extractor filename spellings, stage completion
markers, multiplier idempotency — lives in the scripts only. **Fix bugs in the
scripts, not in C#**, so both entry points benefit.

`scripts/lib/common.ps1` is dot-sourced by every script and holds shared
helpers: settings loading with defaults, `Invoke-MySql`, progress rendering,
`Write-Fail`. It also sets `[Console]::OutputEncoding` to UTF-8 (see gotchas).

### Configuration

`config/settings.psd1` (gitignored, created from `settings.example.psd1`) holds
paths, MySQL credentials, realm details and which core repository to clone.
`Import-ServerSettings` fills defaults for optional keys — necessary because
under `Set-StrictMode -Version Latest` a missing hashtable key **throws** rather
than returning `$null`.

The GUI edits this file surgically through `Psd1Editor` rather than
regenerating it, because the file is mostly explanatory comments meant to be
read and hand-edited.

### GUI project split

| Project | Target | Why |
|---|---|---|
| `WowServer.Core` | `net8.0` | All logic. Compiles and tests on any platform, including Linux CI. |
| `WowServer.Gui` | `net10.0-windows` | WPF shell only. Windows-only build. |
| `WowServer.Core.Tests` | `net8.0` | Console app, no external packages. |

The split exists so the logic can be verified in environments where WPF cannot
build. Keep testable logic in `Core`; keep `Gui` code-behind thin.

Each view creates its own `ScriptRunner` via `Session.Current.CreateRunner()`.
Sharing one instance would deliver every script's output to every pane's
`Output` handler.

## Gotchas that cost real debugging time

Each of these was a shipped bug. The commit messages carry the full reasoning.

**PowerShell**

- `Set-StrictMode -Version Latest` makes a missing hashtable key throw. Optional
  settings keys get defaults in `Import-ServerSettings`.
- `$host`, `$args`, `$input` are automatic variables — never use them as loop or
  local variable names.
- `Set-Content -Encoding UTF8` writes a **BOM** on Windows PowerShell 5.1, which
  breaks AzerothCore's config parser and `mysql < file.sql`. Use
  `Write-TextFileNoBom`, or let the tool write the file (`mysqldump
  --result-file`).
- Under `$ErrorActionPreference = 'Stop'`, `2>&1` on a native command turns
  stderr into a terminating error **before** `$LASTEXITCODE` is read, hiding the
  real message. Relax the preference around such calls, and convert
  `ErrorRecord`s to strings before display or PowerShell's decoration buries the
  actual error.
- A script must `exit 0` explicitly on success if a caller checks
  `$LASTEXITCODE`; otherwise it carries the last native command's value.
- Splatting needs `@variable`. `@($hash.Args)` builds an array and passes the
  hashtable positionally.
- `-File` passes every argument as a plain string: `-Setting a=1,b=2` arrives as
  one string, not an array, so scripts the GUI calls must split on `,`
  themselves. Repeating a parameter (`-Setting a -Setting b`) is a PowerShell
  error, not an array.
- A function returning an empty array yields **`$null`**, because the pipeline
  unrolls it. `return ,@($items)` — the leading comma is load-bearing, and
  without it a caller doing `.Count` fails under StrictMode.
- Writing to `settings.psd1` from PowerShell goes through `Set-ServerSetting`,
  which preserves comments, validates by parsing a temp copy first, and refuses
  to write if the result would contain duplicate keys.

**Process handling**

- The GUI runs scripts with `-NonInteractive`, so `Read-Host` throws. The
  database step asks for the MySQL root password, which is deliberately never
  stored — steps flagged `RequiresInput` open a real console window instead.
- Launching that window with `-NoExit` loses the script's exit code: what comes
  back is the code from closing the window, so a wrong password reads as
  success. Wrap the call in `-Command` and `exit $LASTEXITCODE` instead.

- `Start-Process -PassThru` returns a `Process` whose `ExitCode` reads back as
  `$null` on Windows unless `.Handle` is touched once after starting.
- `CloseMainWindow()` does nothing for a process started with
  `CreateNoWindow` — there is no window.
- Redirecting stdout to a pipe switches the C runtime to 4 KB block buffering. A
  quiet process (the authserver) never flushes; that pane is fed by tailing
  `Auth.log` instead.
- .NET decodes redirected output with the system ANSI code page. Set
  `StandardOutputEncoding` to UTF-8 on both streams, and keep the PowerShell
  side matching.

**Regex on Windows files**

- In multiline mode `$` matches before `\n`, leaving the `\r` of CRLF
  unconsumed. A pattern ending in `$` silently fails to match on
  Windows-authored files. Use `(?=\r?\n|$)`. This one corrupted a real
  `settings.psd1` by appending every key instead of replacing it, producing
  duplicates that `Import-PowerShellDataFile` rejects. **Test string-processing
  code with both LF and CRLF** — literals authored on Linux are LF.

**MySQL**

- Installed silently through winget, so the service is often left on *Manual*
  start and is simply not running after a reboot. The service name carries the
  version (`MySQL80`, `MySQL84`, …) — match on `MySQL*`, don't hardcode.
- Test the **port**, not the service state: `Start-Service` returns as soon as
  the service reports Running, which is before the server accepts connections,
  and a service can be Running while listening somewhere else entirely.
  `Test-MySqlReachable` is a plain TCP connect, which also separates "server is
  down" from "wrong password" — `mysqldump`'s own error does not.
- Ask for elevation only immediately before the call that needs it. Checking
  `Assert-Admin` up front demands a UAC prompt for runs that turn out to have
  nothing to do.
- MySQL 8 defaults to `sql_mode=only_full_group_by`; MariaDB does not. A
  `GROUP BY` that works locally may fail with `ERROR 1055` for the user.
- `MinCount`/`MaxCount` in loot tables are `tinyint unsigned`; writing >255
  fails the whole statement under strict mode, so clamp with `LEAST(255, ...)`
  and report when clamping happens.
- Pass passwords via `MYSQL_PWD`, not `--password`, to avoid the client's
  warning polluting stderr.

**AzerothCore**

- **Each core has its own `MMAP_VERSION`** (master is 20, the Playerbots fork
  is 19). The worldserver does not convert or refuse to start — it rejects each
  tile with "was built with generator vN, expected vM" and silently runs with no
  pathfinding there. Switching cores means regenerating mmaps:
  `05-extract-client-data.ps1 -Only mmaps -Force`. `Get-MmapVersionInfo` reads
  the expected value from `src/common/Collision/Maps/MapDefines.h` and the actual
  one from byte 8 of any `.mmtile`.
- Console commands take no leading dot; in-game chat commands do. Commands that
  need a target or a position (`additem`, `tele`, `npc add`) only work in chat —
  the console has no character and no position. `send items` is `Console::Yes`,
  which is why the heirloom kit goes by mail: max 12 items per letter
  (`MAX_MAIL_ITEMS`).
- Item icons work through `MpqArchive` + `BlpImage` (both in Core, both written
  here): `item_template.displayid` → `ItemDisplayInfo.dbc` → icon name →
  `Interface\Icons\NAME.blp` inside an MPQ → PNG in `Data\icons`. The DBC is
  already on disk — `map_extractor` copies **every** `.dbc`. Only the BLP needed
  a reader. `MpqArchive` handles uncompressed and zlib sectors and refuses
  PKWARE/encrypted loudly rather than returning wrong bytes.
- MPQ header v1 offsets: 14 = sectorSizeShift(u16), 16 = hashTablePos,
  20 = blockTablePos, 24 = hashTableSize, 28 = blockTableSize. Getting these
  wrong reads past the 32-byte header and throws in `BitConverter`.
- MPQ load order cannot be alphabetical: `patch.MPQ` sorts *after* `patch-3.MPQ`
  because `.` > `-`. Rank by the trailing patch number, highest first.
- Item tooltips are built from `item_template` alone. "Use: ..." effects are
  deliberately missing — that text lives in `Spell.dbc`, not the database.
- Heirlooms are `item_template.Quality = 7` (`ITEM_QUALITY_HEIRLOOM`). Query for
  them instead of hardcoding IDs — it picks up whatever modules added, and no
  invented item IDs can creep in.
- Rates like XP, movement speed and profession skill gain live in
  `worldserver.conf`, not the database — `tune-config.ps1` edits it in place and
  snapshots the original values to `configs/.tune-config-original.json` so
  `-Reset` works. Conf changes need a worldserver restart.
- The server silently ignores unknown config keys, so a mistyped key produces no
  error and no effect. `tune-config.ps1` refuses a key that isn't already in the
  file, and every key in `ConfigTuning` was read from `worldserver.conf.dist`.
- There is **no config for crafting tool requirements** — that lives in the
  client's DBC (totem categories), not in `worldserver.conf`.
- Log level numbering is inverted from intuition: higher is more verbose
  (`4` = Info, `2` = Error).
- `server shutdown` rejects a delay of `0` with `LANG_BAD_VALUE`
  ("Incorrect values."). Minimum is 1.
- The extracted data ends up in `<ServerDir>\Data`, **inside** the folder that
  also holds the binaries. Deleting the server folder to start over throws away
  the multi-hour extraction; `reset-server.ps1` deletes around it.
- `mmaps_generator` resolves `maps/` and `vmaps/` **relative to its working
  directory** and takes no path argument (only `--config`). After a finished
  install those folders live in `Data\`, so re-running `-Only mmaps` from the
  client folder dies with "'maps' directory is empty or does not exist" (exit
  -3). `Get-MmapsWorkDir` picks whichever directory holds both, and the output
  then lands straight in `Data\mmaps`.
- The move-to-`Data\` step must never let an empty source folder replace a
  populated destination: with `-Force` a leftover empty `client\mmaps` would
  delete hours of freshly generated tiles.
- A stage counts as already extracted if its folder in `Data\` has files.
  Checking only the client folder — which is where extraction runs, before the
  results are *moved* to `Data\` — made a finished install look untouched and
  re-ran the mmaps pass. No marker file is written into `Data\`: the worldserver
  reads that directory.
- Extractors are named `map_extractor.exe` / `vmap4_extractor.exe` /
  `vmap4_assembler.exe`; older docs say the underscore-less spellings. Accept
  both.
- Module SQL is applied automatically by the worldserver's updater. Many module
  READMEs still say to import it manually — that is outdated and risks
  duplicates.
- Most modules have no root `CMakeLists.txt`; the core aggregates them. Don't
  use its presence to detect an installed module.
- Some modules pull dependencies as git submodules — clone with
  `--recurse-submodules`. A module cloned without it *looks* installed: the
  directory is there, the submodule path is an empty folder, and the build dies
  half an hour later with `lua.h: No such file or directory`.
  `Get-EmptySubmodulePath` catches that up front; `rebuild.ps1` calls it.
- Playerbots and NPCBots are **not modules**: each requires replacing the core
  with a fork. Installing them over the stock core produces dozens of `C2660`
  errors. Switching forks deletes the source tree, and `modules/` lives inside
  it. `rebuild.ps1` refuses to start a build in that state (`-Force` overrides),
  because the failure otherwise costs a full compile to discover.
- Playerbots moved from `liyunfan1223/*` to the `mod-playerbots` org. GitHub
  redirects the old URLs, so a core cloned from either address is the same code.
  `ModuleCatalog.SatisfiesFork` accepts both — treating the old one as "wrong
  core" would trigger a reclone that **deletes the source tree**.

- The module catalogue in `ModuleCatalog.cs` is curated, not exhaustive. Anything
  else installs through the URL box, validated by `CustomModuleUrl`. Every
  catalogue entry's `Name` must equal the tail of its `Repository` — that name is
  the folder under `modules/`, and the tests enforce it.
- Every URL in the catalogue was checked with `git ls-remote` before being added.
  Several plausible-sounding repos do not exist under the `azerothcore` org
  (`mod-reagent-bank` and `mod-individual-progression` are `ZhengPeiRu21`'s,
  `mod-assistant` is `noisiver`'s). Check before adding, don't assume the org.

**Comparing repository URLs**

- The same origin appears as https, ssh (`git@github.com:owner/repo.git`), with
  embedded credentials, or behind a proxy. Compare the `owner/repo` tail, not
  the whole URL. Both implementations of this rule are tested:
  `ModuleCatalog.SameRepository` (C#) and `Test-MesmoRepo` in `rebuild.ps1`.
- Strip the trailing `/` **before** the `.git` suffix — `...repo.git/` leaves the
  suffix unmatched in the other order.
- `s.Split('/', ':', StringSplitOptions.RemoveEmptyEntries)` compiles but binds
  to `Split(char, int, StringSplitOptions)`: `':'` converts implicitly to `int`
  and becomes `count`. Pass `new[] { '/', ':' }`. This silently broke ssh URLs.

**WPF**

- Setting a property like `IsChecked="True"` in XAML raises its changed event
  during `InitializeComponent`, when elements declared later do not exist yet.
  Set such initial values in the constructor.
- `TextWrapping="Wrap"` inside a `ListBox` needs
  `ScrollViewer.HorizontalScrollBarVisibility="Disabled"` — that is what
  constrains the item to the viewport width.
- Every input control carries a `<v:HelpIcon Chave="..."/>`. The text lives in
  `FieldHelp` (Core), not the XAML, so the tests can assert every entry has
  explained examples **and** that every `Chave` used in any `.xaml` resolves —
  a typo there would otherwise surface as an empty tooltip on the user's screen,
  since the WPF project does not compile in this environment.
- Views are built before `Session.Current.Loaded` exists, so anything derived
  from settings must be recomputed on `Loaded` **unconditionally**. Guarding
  that refresh with `if (Session.Current.Loaded is null)` looks like an
  optimisation and is a bug: another view's `Loaded` may have filled it in
  first, and then the guard skips the redraw for the one case that needed it.
  This is what hid the "Corrigir o core" button for three rounds. Views also
  refresh on `IsVisibleChanged`, so state changed by a script outside the GUI
  shows up on returning to the tab.
- **`settings.psd1` is intent; the git remote is fact.** They diverge whenever a
  core switch writes the settings and then fails to clone. Anything that decides
  whether the installed core is right must read
  `git -C <SourceDir> remote get-url origin`, as `rebuild.ps1` always did — the
  Modules screen compared against the settings instead, and so reported
  everything fine while the build kept refusing. Show both when they disagree.

## Working conventions

- **Verify claims against sources.** AzerothCore behaviour was repeatedly
  confirmed by reading its actual source or config files rather than relying on
  memory or wiki summaries, several of which are outdated.
- Tuning scripts snapshot original values into a backup table and compute every
  update from that snapshot, so applying `3x` twice stays `3x` and `-Reset`
  restores exactly. Preserve that property in anything similar.
- Destructive or long operations preview by default and require an explicit
  flag to act.
- Commit messages explain *why* the change exists, and state plainly what was
  verified and what was not.

## Environment constraints

This repository is normally worked on from a Linux container while the user runs
Windows. That means:

- The WPF project **cannot be compiled here**. Verify what is verifiable:
  `WowServer.Core` builds and its tests pass, every `.xaml` parses as XML, and
  every `x:Name` referenced in code-behind exists in its XAML. Say clearly that
  the GUI itself was not compiled.
- PowerShell scripts can be parse-checked with
  `[System.Management.Automation.Language.Parser]::ParseFile` under `pwsh`, but
  Windows-specific behaviour (5.1 error records, `Start-Process` exit codes,
  registry, elevation) cannot be reproduced.
- MySQL logic can be exercised by installing MariaDB locally — but start it with
  MySQL 8's `sql_mode` to catch strictness differences.
- The user's machine is unreachable. Deliverables are scripts and code they run
  themselves.
