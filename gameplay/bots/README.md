# Deathmatch bots

Select Deathmatch or Team Deathmatch on Dust II, Inferno or Mirage
(`de_cpl_strike`), then choose **Bots > Difficulty**
and a nonzero **Quota** in the existing create-game dialog. The default
Join after player waits until a human chooses a playing team. FFA attacks any
other playing player; TDM ignores teammates. Other maps and Free Play do not
spawn these bots. Menu edits configure the next match; console edits configure
the current match. Map restart retains live settings.

## Console

Commands use the existing console parser, validation, `help`, and Tab completion.
Only the host can change settings in LAN. Numeric commands without arguments
show their value and help. Difficulty, team, prefix and weapon allowances apply
to newly created bots/next spawns; stop and don't-shoot take effect immediately.

| Command | Meaning |
| --- | --- |
| `bot_add`, `bot_add_t`, `bot_add_ct` | Add one bot, optionally choosing its team |
| `bot_kick [name\|all]` | Remove a named bot or every bot; quoted names may contain spaces |
| `bot_quota 8` | Set desired population (0–15) |
| `bot_quota_mode normal\|fill\|match` | Fixed bots, fill total players, or bots per human |
| `bot_difficulty 0..3` | Easy, normal, hard, expert; existing profiles remain unchanged |
| `bot_join_team any\|t\|ct` | Team for new bots |
| `bot_join_after_player 0\|1` | Wait for a human on a playing team |
| `bot_prefix "[BOT] "` | Prefix for newly added bot names; at most 32 characters |
| `bot_allow_pistols 0\|1` | Allow Deagle/USP on the next spawn |
| `bot_allow_rifles 0\|1` | Allow AK-47/M4A1 on the next spawn |
| `bot_allow_snipers 0\|1` | Allow AWP/Scout on the next spawn |
| `bot_stop 0\|1` | Stop movement; perception/firing continue |
| `bot_dont_shoot 0\|1` | Disable attacks |
| `bot_status` | Population, settings, and each bot's state/HP/shots/kills/deaths |

Example: `bot_difficulty 2; bot_quota 8`. `bot_kick` also sets quota to zero,
so the quota manager does not immediately recreate kicked bots. Fill/match
counts include connected humans and are bounded by the shared player limit.

## Source fidelity and limits

`assets/bots/profiles.json` contains 140 original personalities extracted from
the installed CS:S `cstrike_pak_dir.vpk:botprofile.db`. Defaults, template
inheritance, names, skill, aggression, reaction time, attack delay, weapon
preferences and skins are retained. The reference DB and SHA-256 provenance are
saved by `tools/source_import/import_css_bot_profiles.py`.

The C# controller adapts the angular spring/damper (300/30 attacking,
200/25 otherwise, acceleration cap 3000 degrees/s²), range-dependent aim error,
2–5-second focus, attack delay and 360/400/800-unit firing thresholds from
`cs_bot_vision.cpp` / `cs_bot_weapon.cpp`. Reference copies are under
`tools/source_import/references/bots`; their source is
<https://github.com/nillerusr/source-engine/tree/master/game/server/cstrike/bot>.
That repository is a third-party source mirror, not an official Valve CSS SDK.
These components have been adapted for Godot, not validated as a complete
frame-for-frame reproduction of CSS AI.

Bots use the project's actual `Player.Simulate`, movement, collision, shared
firearms, ammo/reload/deploy timers, recoil/spread, traces, armor, hitboxes,
death/score and DM respawn behavior. They do not set opponent health directly.
Perception uses a 90-degree view cone, body/head visibility samples, flash/smoke
occlusion, delayed recognition, last-seen memory and uncertain sound positions.
Hidden opponents are not continuously tracked. Moving bots follow baked
navigation, separate from nearby players, try sidesteps/jumps when stuck and
check floors before advancing. Hearing comes from actual sound events.

Navigation is baked separately against each supported map's imported world and player clips;
it is **not** an importer of CSS `.nav` tactical annotations. Ladder routing,
crouch-only passages, tactical cover analysis, bomb/hostage tasks, radio chatter,
voice selection and taking control of a bot are not implemented. Their reserved
menu controls are disabled. Only weapons already present in this project are
supported; with all firearm categories disabled bots use their knives. DM
disables grenades. Harmless difficulty moves but never attacks. Aggression
affects crouch-and-hold probability and the 2–4-second wait before chasing a
lost enemy, with the original extra patience for snipers/crouching. Dodge
probability uses 80% of skill and increases against a visible sniper. Teamwork
and retreat/cover tactics are not a full port of the original state machine.

## Regeneration and checks

```powershell
python tools/source_import/import_css_bot_profiles.py
& $godot --headless --audio-driver Dummy --path . --script tools/source_import/build_bot_navigation.gd
dotnet build --no-restore
python tools/source_import/verify_bots.py --godot $godot
python tools/source_import/verify_bots.py --godot $godot --map de_inferno
python tools/source_import/verify_bots.py --godot $godot --map de_cpl_strike
python tools/source_import/verify_bots.py --godot $godot --lan
& $godot --headless --audio-driver Dummy --path . res://scenes/debug/bot_checks.tscn -- --menu-test-profile --console-test-profile --bot-soak
```

The real-map checks exercise menu setup, patrol movement, visibility and FOV,
weapon ammunition/damage, team rules, death/respawn, console restrictions,
autocomplete, loadout restrictions and restart. The two-process LAN checks
exercise initial roster, presentation-only replicas, motion, death/respawn and
client command rejection. `--bot-soak` runs a thirty-second eight-bot match.
Test profiles keep ordinary console/menu configuration separate.
Use `--bot-map=de_inferno` or `--bot-map=de_cpl_strike` with the direct Godot
command to run the same checks or soak on those maps. The navigation builder
accepts map IDs after `--`; with no arguments it rebuilds all three maps.
