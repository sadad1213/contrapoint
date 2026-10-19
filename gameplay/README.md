# Local match setup

The create-game dialog follows the installed CS:SO 2.0 `CSCreateServerDialog`
resources and its GameUI `SourceScheme`. Original resources and English labels
are in `tools/source_import/references/csso`. The original 640x480 layout is
widened by 260 pixels for a map preview; the original field coordinates and
252-pixel columns are preserved. The main screen remains the project's CSS menu.

`GameModeCatalog` owns game types, stable mode IDs, map compatibility and launch
availability. `sandbox` (Free Play), `deathmatch` (FFA) and `teamdeathmatch`
(T vs CT) are playable. Casual, Competitive, Wingman, Arms Race, Demolition
and Training are future entries, hidden from the mode selector along with types
that have no available modes. Saved unavailable selections fall back to Free Play.
Implement their rules before setting `Available` to true. Map wrappers still
come from the shared `MapCatalog`, also used by the console and LAN.

`MatchSession.Draft` stores the next local game's settings. Closing the selector
or launching saves it to `user://match_setup.json`. `Begin` captures an immutable
`Current` setup once scene loading succeeds. The local player's initialization
applies pending friendly-fire, fall-damage and cheat settings after console
configuration. Editing the draft during a match does not change that match.
Console/LAN player initialization without a pending menu launch clears the
menu snapshot. LAN negotiates mode, server settings, clock and scores on join
and replicates live changes. Clients cannot change these server settings.

Bot options persist as `BotSetup`. DM/TDM bots on Dust II, Inferno and Mirage (`de_cpl_strike`) use these settings;
see [bots/README.md](bots/README.md) for commands, behavior and verification.
Server name, password
and maximum-player fields preserve the reference layout and remain disabled
until the menu has a server hosting flow. Existing console LAN commands work
as before. Extend the launch snapshot and hosting flow when these are added.

Validation (Mono Godot, after `dotnet build`):

```powershell
& $godot --headless --path . --script tools/source_import/verify_menu.gd -- --menu-test-profile --console-test-profile
& $godot --path . --script tools/source_import/render_menu.gd -- --menu-test-profile
```

The test profiles keep ordinary saved setup and console configuration separate.
The checks exercise dropdowns, filtering, all map previews, persistence, launch
snapshots, actual Dust II/Inferno loads and pause-menu map switching. The render
script captures every tab, DM and an 800x600 window to `user://`.

## Deathmatch

Select **Arsenal > Deathmatch** or **Team Deathmatch**, select the map, start,
and choose a team/class. `lan 1` opens the existing LAN server; clients use
`connect <host>`. FFA treats all other playing players as enemies, including
players wearing the same team's models. TDM uses T/CT and the Friendly fire
setting. The radar does not reveal same-model enemies in FFA.
Both DM modes give no grenade ammunition, including after respawns and map
restarts; the grenade slot cannot be selected. Free Play retains its grenades.

Defaults: 10-minute map, 1-second automatic respawn, 100 armor and helmet,
2-second spawn protection canceled by an attack, +15 HP (up to maximum health)
and +15 rounds in the active firearm's magazine (up to its capacity) per enemy
kill. Ammunition rewards never consume reserve ammo. Suicides, teamkills and
duplicate lethal hits give no reward. A dead attacker cannot be healed/revived
by a delayed grenade kill. Dust II, Inferno and Strike use dedicated map-wide
`Spawn_deathmatch_*` markers for both FFA and TDM. Points are baked offline from
walkable navigation reachable from the original team bases, with standing-hull
and floor checks; no navigation baking happens during a match. KZ is unchanged.
Each respawn samples shuffled points, rejects occupied/obstructed positions and
missing floors, prefers cover from living opponents, distance and a different
area from the previous spawn. The host owns selection and replicates its result.
Maps without DM markers retain the original team-spawn selection. Free Play
continues to use its original team points, and explicit `SetSpawn` overrides
also disable DM selection.

To regenerate combat-map points after changing map collision (Mono Godot):

```powershell
& $godot --headless --audio-driver Dummy --path . --script tools/source_import/build_deathmatch_spawns.gd
# Or append: -- de_dust2
```

The generated `assets/maps/MAP/deathmatch_spawns.tscn` is instanced under the
native map root in its playable wrapper. New combat maps can use the same tool
and wrapper pattern; movement maps are rejected by the generator.

The stock lower CSS HUD displays actual armor/ammunition and authoritative
match time. TAB uses the installed CSS scoreboard's `scoreboard.res`, background,
selection texture, avatars, dead icon, Verdana fonts, team colors and 480-unit
coordinates. It shows live kills/deaths, team totals and measured ENet RTT.
There is no permanent custom DM text above the lower HUD. The importer is
`tools/source_import/import_css_scoreboard.py`; original files and asset hashes
are retained under `references/scoreboard` and `assets/ui/css/scoreboard`.

Host console settings (DM extensions are explicitly prefixed `dm_`):

| Command | Default | Effect |
| --- | --- | --- |
| `mp_timelimit` | `10` | Minutes, including elapsed time; `0` disables the clock |
| `mp_teammates_are_enemies` | mode selection | `1` FFA, `0` TDM |
| `mp_friendlyfire` | menu setting | Team damage in TDM; same-model enemies in FFA take full damage |
| `dm_respawn_delay` | `1` | Automatic and earliest manual respawn, seconds |
| `dm_spawn_protection` | `2` | Invulnerability after spawning, seconds; `0` disables |
| `dm_spawn_armor` / `dm_spawn_helmet` | `100` / `1` | Armor and helmet on next spawn |
| `dm_kill_health` / `dm_kill_ammo` | `15` / `15` | Health and magazine rewards |
| `dm_restart_delay` | `5` | Countdown when map time expires |
| `mp_restartgame N` | — | Restart after 1–60 seconds; `0` cancels a scheduled restart |
| `dm_status` | — | Current mode, time, team score and restart state |

Server settings survive map restarts in the running process; put desired
defaults in `cfg/autoexec.cfg` for persistence between launches. DM settings
are not written into clients' personal configs. Mode selection chooses FFA/TDM
at launch; `mp_teammates_are_enemies` can switch the running match.

Expiration reloads the actual map scene after the countdown, resetting map
entities, effects, corpses, scores, health and ammunition. ENet connections,
peer IDs, team/model selection and chosen weapons survive. Map generations
reject queued poses, attacks, world states, smoke and grenade packets from the
previous load. Normal `map`/menu map changes still close LAN.

Validation:

```powershell
powershell -File tools/run_lan_checks.ps1 -Godot $godot -Deathmatch -Port 27186
& $godot --headless --audio-driver Dummy --path . res://scenes/debug/deathmatch_spawn_checks.tscn -- --console-test-profile --menu-test-profile
& $godot --headless --audio-driver Dummy --path . res://scenes/debug/deathmatch_checks.tscn -- --console-test-profile --menu-test-profile --dm-role=offline
```

The three-process check exercises actual shot ammunition, kill rewards,
duplicate deaths, FFA/TDM, friendly fire, armor/respawns, client restrictions,
late joining, automatic reload, a second console reload and stale-map packets.
Add `--dm-preview` to a rendered offline check to save `.godot/dm-preview.png`.
