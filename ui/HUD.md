# CSS HUD

DE round results use `CssRoundPanel` and the installed CS:S `win_round.res`:
220 x 138 at `c-110, 287`, team-colored Verdana Bold title, localized reason,
original star/avatar/glow artwork and the three inset panels. The glow uses the
original material's additive blend. TAB hides the panel. Source textures,
layout, materials and hashes are preserved under `assets/ui/css/round` and
`tools/source_import/references/round`; re-extract with Blender running
`tools/source_import/import_css_round_panel.py`.

`DefuseRoundSummary` selects the successful planter/defuser or the winning
team's top enemy killer for MVP and replicates the summary with the round state.
Fun facts currently cover enemy kills and short rounds; the remaining Source
fun-fact categories and Steam avatars are not implemented. Use
`scenes/debug/round_panel_checks.tscn` for result/localization, snapshot,
proportional geometry and fade checks, plus rendered previews in `.godot`.

`HUD/Status` in the shared player scene renders health, armour/helmet, round time,
money and ammunition. Original CSS fonts, sprite atlas and proportional positions
come from the installed game's `clientscheme.res`, `hudlayout.res` and
`mod_textures.txt`. Backgrounds use normal alpha blending; foreground glyphs and
the sprite atlas use Source's additive blending. Coordinates scale with screen
height against 480, independently of Godot's UI stretch.

`HudSnapshot.Capture` is the presentation boundary. Health and ammunition are live.
During first-person observation, the local HUD, radar, crosshair and scope use
`Player.HudPlayer`, the selected living player. See [observation](../player/OBSERVER.md)
for controls, passive weapon rendering and LAN checks.
`Player.Armor` and `Player.HasHelmet` reflect weapon/HE damage; DM spawns grant
100 armor and a helmet by default. `Player.Money` is the authoritative buy balance.
The stock green `shopping_cart` glyph (`c` in `cstrike.ttf`) appears at the
original `HudShoppingCart` position `(64, 240)` whenever buying is available.
It has no accompanying text or key prompt, and hides for dead players/observers.
`RoundState` supplies the authoritative DM map countdown; LAN clients receive
it from the host. The HUD does not run a second countdown. Free Play has no
running clock. Damage is calculated in `PlayerDamage`.

DM's TAB uses the installed game's `scoreboard.res`, original background and
selection textures, default team avatars, dead icon and Verdana fonts. Team
sections, columns, colors and proportional coordinates follow the resource.
The lower HUD has no permanent custom mode label. See [deathmatch settings and
checks](../gameplay/README.md#deathmatch) and `import_css_scoreboard.py`.

Firearms own their magazines. `PlayerInventory` owns shared reserves by ammo type.
Reload transfers available rounds when the existing reload animation finishes;
holstering cancels it. Empty magazines reload automatically, empty reserves cannot
create bullets, and respawn restores the starting loadout's ammunition. AK and
Scout share 7.62 mm reserve. The existing client-controlled LAN combat model is
unchanged; server-authoritative ammunition validation remains future networking work.

Radar reads original overview origins/scales from
`assets/ui/css/hud/overviews.json`. It resolves the map through the player's
`SpawnPointsRootPath` scene, displays stock Dust2/Inferno/CPL Strike radar
textures plus the CS:GO Cache radar, rotates with player yaw, and marks living
teammates. Maps without a registered texture and overview hide the radar. The
Cache radar is `assets/ui/css/hud/de_cache_radar.png` with the original
`de_cache` overview transform (`pos_x -2000`, `pos_y 3250`, `scale 5.5`), which
the CSSO install ships as `materials/overviews/de_cache_old_go_radar.vtf` and
`resource/overviews/de_cache_old_go.txt`. Enemy spotting, bomb/hostage state,
radio flashes, and named navigation areas need future gameplay inputs.

Console controls: `cl_drawhud 0/1`, `hideradar`, `drawradar`,
`cl_radaralpha 0..255`, `cl_radar_locked 0/1`.

Re-extract assets with Blender:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --python tools/source_import/import_css_hud.py
```

The script refreshes the stock CSS textures and overview transforms and keeps
the radar entries it cannot extract itself, so the CPL Strike and Cache radars
survive a re-run.

Source file hashes are recorded in `assets/ui/css/hud/provenance.json`. Reference
layout files are preserved in `tools/source_import/references/hud`.

Verification (Godot Mono 4.8 beta 1, after `dotnet build` and asset import):

```text
Godot --headless --audio-driver Dummy --path . --fixed-fps 66 scenes/debug/hud_checks.tscn -- --console-test-profile
Godot --audio-driver Dummy --path . --windowed --script tools/source_import/render_hud.gd -- --console-test-profile
```

The render script writes 720p, 1080p and 4:3 low-health previews into `.godot`.
Related checks: combat, player, console, recoil, spread, player presentation and
LAN. Player presentation needs real-time frames (`--max-fps 240`), not `--fixed-fps 66`.
