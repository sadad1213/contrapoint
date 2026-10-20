# Source menus

`B` opens the CSS buy menu. Original `BuyMenu_*.res`, item cards, localized
strings, artwork, fonts, sounds and prices are imported by
`tools/source_import/import_css_buy_ui.py`; sources and hashes are retained in
`tools/source_import/references/buy` and `assets/ui/css/buy/provenance.json`.
The layout uses Source's 640 x 480 proportional coordinates, with mouse and
original category/item number shortcuts. Escape/B/0 closes it without pausing;
Backspace/Left returns to categories. Unsupported categories/items are disabled.
The installed USP, Desert Eagle, AK-47, M4A1, Scout and AWP are available to both
teams. Quick buy, rebuy and four saved favorites use the same purchase rules.
The obsolete weekly market entry remains disabled.

DM/TDM firearms are free and the selected loadout survives respawn. Existing
DM grenade restrictions remain. Custom/Free Play starts with knife, USP and
$16000; buying spends CSS prices, and death restores the balance to $16000.
Only the starting equipment returns automatically in Custom. Buying works
anywhere while alive; these modes have no round buy timer or buy-zone restriction.
Firearms replace their slot and drop the old gun; grenades use CSS carry limits.
The LAN host validates purchases and reliably replicates inventory, balance and
results, including late joins. Clients cannot supply their own prices/balances.

Checks (use the installed Godot .NET executable):

```text
dotnet build --no-restore
Godot --headless --audio-driver Dummy --path . res://scenes/debug/buy_menu_checks.tscn -- --console-test-profile --menu-test-profile
powershell -File tools/run_buy_checks.ps1 -Godot <Godot.exe>
Godot --audio-driver Dummy --path . res://scenes/debug/buy_menu_checks.tscn -- --console-test-profile --menu-test-profile --buy-preview
```

The preview saves main/rifle/equipment screens and the HUD cart in `.godot`,
covering 1280 x 720, 1920 x 1080 and 1024 x 768. Test profiles isolate saved
console/setup/favorite settings from normal play.

The application starts at `scenes/main_menu.tscn`. `MainMenu.cs` recreates the CSS
desktop menu with **Play**, **Options** and **Quit**. Play opens the CS:SO server
layout, adapted to three playable maps: `de_dust2`, `de_inferno`, and `kz_jayverb000`.

Original references are preserved in `tools/source_import/references/gameui/`:

- CSS `ClientScheme.res`: title at `(32, 180)`, SOURCE at `(380, 205)`, menu at
  `(32, 248)`, `cslogo.ttf` at size 60. Title/menu coordinates scale with height
  against Source's 480-unit reference.
- `SourceSchemeBase.res`: Verdana 12 for the menu, Tahoma for dialogs, 22-unit
  menu spacing, white/gray menu states, orange `255 155 0` chapter selection,
  translucent gray frames, raised/sunken borders and dimmed modal background.
- HL2 `NewGameDialog.res`: 600 × 296 desktop frame, original divider and button
  coordinates. `NewGameChapterPanel.res`: 168 × 106 picture border, 152 × 86 image,
  and two labels. Cards are 184 units apart, ordered left to right. Lists with
  up to three maps stay fixed from the left; longer lists center the selected card.
- [BasePanel.cpp](https://github.com/nillerusr/source-engine/blob/master/gameui/BasePanel.cpp)
  and [NewGameDialog.cpp](https://github.com/nillerusr/source-engine/blob/master/gameui/NewGameDialog.cpp):
  original layout and selection behavior, including double-click to play.

Backgrounds, logo font, map thumbnails and UI sounds come from the local CSS
installation. `tools/source_import/import_css_menu.py` reproduces extraction in
background Blender with SourceIO; `assets/ui/css/provenance.json` records source
paths and hashes. Thumbnail padding is excluded through an AtlasTexture region.
Both standard and widescreen backgrounds are included; the original aspect-ratio
threshold is 1.5999. Dialogs shrink only when the viewport cannot contain them.

In game, **Escape** opens the menu and pauses offline simulation; another Escape resumes.
LAN sessions continue simulating while menus are open; see [LAN](../network/README.md).
Escape or Cancel in map selection returns to the menu. Left/right selects a map;
Play, Enter on a card, or a double-click starts it. Threaded resource loading
displays progress, then switches to the existing map scene. Console input takes
precedence over opening the pause menu.

With a loaded map, archived console settings (including mouse sensitivity),
bindings and aliases save automatically to `user://cfg/config.cfg` after each command batch. Startup
loads the saved config before `autoexec.cfg`; changing maps keeps runtime values.
`host_writeconfig [name]` remains available for exporting a named config.

## CSSO Options

Options uses the installed **Counter-Strike: Source Offensive** `CSOptionsDialog`
resources: a native-pixel 640 × 480 frame, vertical Game/Controls/Audio/Video
sections and the original nested pages and field positions. Run
`python tools/source_import/import_csso_options.py` to reproduce the import;
original resources and source hashes are in `tools/source_import/references/options`.
Disabled controls explicitly identify CSSO features that have no implementation
here (voice transport, music categories, Source HDR/shader presets, motion blur,
HD content and unsupported HUD/gameplay variants).

Mouse, key/mouse-button bindings, master volume, crosshair preview/settings,
viewmodel FOV/bob, supported gameplay preferences and rendering settings share
the existing console archive. Apply updates live values and writes
`user://cfg/config.cfg`; Cancel/Escape discard the current draft. The same
preferences apply before loading a map, after changing maps and after restart.
Offline Options preserves the pause state; LAN menus keep the match running.

Rendering controls change world/weapon MSAA, mesh LOD thresholds, texture mip
detail/filtering (including embedded transparent/two-sided map shaders), dynamic
shadow resolution/filtering, water shading, exposure, VSync and FPS cap.
Imported map lightmaps retain baked shadows. Model detail affects meshes with
imported LODs. Texture detail/filter changes update uniforms on shared shaders,
avoiding fresh shader variants each time. Bilinear uses a single mip level and
retains the selected texture detail. UI textures/lightmaps remain full quality.

Startup preserves native 3D rendering resolution. Older configs containing the
initial 1280 × 720 defaults also keep native resolution until a resolution is
explicitly selected. A deliberate lower fullscreen resolution scales both world
and weapon render passes; UI stays native. Display changes have a 15-second
Keep/Revert confirmation and are saved only after Keep.

Regression checks use `--options-test-profile`, isolated from real player settings:

```text
dotnet build --no-restore
Godot --headless --audio-driver Dummy --path . res://scenes/debug/options_checks.tscn -- --options-test-profile --menu-test-profile
Godot --headless --audio-driver Dummy --path . res://scenes/debug/options_checks.tscn -- --options-test-profile --menu-test-profile --options-read
Godot --audio-driver Dummy --path . res://scenes/debug/options_checks.tscn -- --options-test-profile --menu-test-profile --options-display-check
Godot --audio-driver Dummy --path . res://scenes/debug/options_checks.tscn -- --options-test-profile --menu-test-profile --options-preview
Godot --audio-driver Dummy --path . res://scenes/debug/options_map_checks.tscn -- --options-test-profile --options-native-preview
```

Map checks inspect every native Dust II material and compare actual Low/High/Low
rendered frames. Use `--options-map=de_inferno` to inspect another native map.

`cleanup` removes world weapon pickups (including dropped guns and grenade pickups),
preserving map props and player inventories. It prints the number removed, needs
no cheats, and in LAN can only be run from the host console; removals replicate
to connected clients.

Persistence checks use an isolated console test profile. Run the writer and then
the reader in separate processes to cover both player recreation and restart:

```text
Godot --headless --audio-driver Dummy --path . res://scenes/debug/settings_checks.tscn -- --console-test-profile
Godot --headless --audio-driver Dummy --path . res://scenes/debug/settings_checks.tscn -- --console-test-profile --settings-check-read
```

Verification:

```text
dotnet build --no-restore
Godot --headless --audio-driver Dummy --path . --script tools/source_import/verify_menu.gd
Godot --headless --audio-driver Dummy --path . res://scenes/debug/console_checks.tscn -- --console-test-profile
Godot --audio-driver Dummy --path . --script tools/source_import/render_menu.gd
```

The menu integration check loads all three maps, exercises pause/resume and
console interaction, and finishes through the real Quit callback. The render
script writes menu and map-selection screenshots to Godot's `user://` directory.

## CSSO kill feed

`CssoKillFeed` imports the installed CSSO `HudDeathNotice` block from
`scripts/hudlayout.res`, `HUDDeathNoticeNameFont` / Stratum2 Bold from
`resource/clientscheme.res`, 18 SVG weapon/condition icons and four original
corner masks. `tools/source_import/import_csso_killfeed.py` reproduces the
import in Blender/SourceIO; original resources and SHA-256 records are retained.
CSSO's proportional base is 1080, with a default 1.25 multiplier in Contrapoint
for readability. Rows show team colors, a red border for your kills and a red
background for your deaths. The imported limit is 16 entries.

Authoritative deaths from bullets, knives, HE and world/fall damage generate
one notice each. Actual attacks retain their weapon and scope state; flags
cover headshots, penetration, no-scope, blind shots, smoke and airborne shots.
Smoke detection uses the shot segment and the current growing smoke volume.
Assists and domination/revenge are not generated because their game rules have
not been implemented. CSSO's closed client logic is not copied: ordering,
six-second lifetime and final half-second fade are implemented locally around
its imported presentation data.

`hud_deathnotice_time 6` sets lifetime; `0` disables notices.
`hud_deathnotice_scale 1.25` controls text/icon/row size together (Contrapoint
extension, saved in the local config). `cl_drawhud 0` also hides the feed.
Notices survive respawn, update above the death freeze frame, and are excluded
from its captured image so expired notices cannot leave a frozen ghost.

`killfeed_checks.tscn` exercises lethal traces, scope/headshot metadata,
duplicates, HE, knife, fall, flash, smoke, limits and expiry. Run with
`--killfeed-preview` on a rendered viewport to save `.godot/killfeed_720.png`
and `.godot/killfeed_1080.png`. LAN checks also verify reliable delivery and
the absence of duplicate knife kill notices.

## Cheat command restrictions

`sv_cheats` defaults to `0`; only the LAN host can change it. With a loaded map,
use `sv_cheats 1; noclip` or `sv_cheats 1; thirdperson` to enable debug modes.

LAN clients can use `god [0|1]` and `ent_create weapon_<name>` when the host
enables `sv_cheats 1`. The server applies god mode to the requesting player
and creates weapon entities at that player's aim, replicating them to all peers.

The following existing commands/settings require `sv_cheats 1`:

- `noclip`, `thirdperson`, `stopsound`, `god`, `buddha`, `hurtme`;
- `cl_drawhud`, `r_drawviewmodel`, `cl_bob`, `cl_bobup`;
- `spec_freeze_time`, `spec_freeze_traveltime`, `spec_freeze_distance_min`,
  `spec_freeze_distance_max`, `player_debug_print_damage`;
- Contrapoint extensions `respawn` and `cl_playermodel` (the latter also changes
  the player's team). Normal team/class selection remains available.

Queries remain available while cheats are off. Aliases, binds, configs, `toggle`
and `incrementvar` go through the same protection. `firstperson`, radar controls,
`viewmodel_fov`, mouse/crosshair settings and movement tuning such as `sv_gravity` and
`sv_airaccelerate` do not require cheats.

`viewmodel_fov` is saved automatically and restored after reconnecting or restarting.

CSSO weapon motion can be switched live with `cl_bob_style 0` (CS:S)
or `cl_bob_style 1` (CS:GO, default), or `toggle cl_bob_style`.
The switch and `cl_bobcycle`, `cl_bobcycle_old`, `cl_bobamt_vert`, `cl_bobamt_lat`,
`cl_bob_lower_amt`, `cl_viewmodel_shift_left_amt`, `cl_viewmodel_shift_right_amt`,
`cl_wpn_sway_scale` and `cl_wpn_sway_interp` are saved and do not require cheats.
See [viewmodel motion](../player/VIEWMODEL.md) for defaults and source details.

`sv_cheats 0` clears god/buddha, player noclip and third-person camera, resets
cheat variables to their project defaults, and drops their saved runtime overrides.
Spectator flight is preserved. LAN rule updates apply the same reset on clients;
clients cannot enable `sv_cheats` locally. Cheat presentation variables are not
archived in `host_writeconfig`.

Flags were checked against the CSS paths in the Source engine mirror:
[server/client.cpp](https://github.com/nillerusr/source-engine/blob/master/game/server/client.cpp),
[client/in_camera.cpp](https://github.com/nillerusr/source-engine/blob/master/game/client/in_camera.cpp),
[client/view.cpp](https://github.com/nillerusr/source-engine/blob/master/game/client/view.cpp),
[shared/cstrike/weapon_csbase.cpp](https://github.com/nillerusr/source-engine/blob/master/game/shared/cstrike/weapon_csbase.cpp),
[client/viewrender.cpp](https://github.com/nillerusr/source-engine/blob/master/game/client/viewrender.cpp),
[client/clientmode_shared.cpp](https://github.com/nillerusr/source-engine/blob/master/game/client/clientmode_shared.cpp),
[server/player.cpp](https://github.com/nillerusr/source-engine/blob/master/game/server/player.cpp),
[engine/audio/snd_dma.cpp](https://github.com/nillerusr/source-engine/blob/master/engine/audio/snd_dma.cpp)
and [shared/movevars_shared.cpp](https://github.com/nillerusr/source-engine/blob/master/game/shared/movevars_shared.cpp).
