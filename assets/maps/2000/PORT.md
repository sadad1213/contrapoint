# $2000$ — Calou

Playable scene: `res://scenes/maps/2000.tscn`.
Select `$2000$` in Create Server, or run `map 2000` / `map $2000$`.
Free Play, Deathmatch, Team Deathmatch and bots are supported.

Source: `C:/Users/Hueglot/Downloads/2000css/cstrike/maps/$2000$.bsp`.
SHA256: `42aae0ec0fa8274fc6d4437a750f06d6b6a2f8c30238c6d9467e2d64f0c5f945`.

The port preserves original LDR face lightmaps, embedded textures, the Tides sky,
16 floodlights with PHY collision and glow sprites, 66 compiled world brushes,
16 T and 16 CT spawns with authored angles, both buy zones, and nine breakable
glass skylights. There are 69 generated reachable DM spawns and a baked bot mesh.

33 original pickups use the project's weapon system: 10 AK-47, 14 M4A1, four AWP,
four HE grenades and one flashbang. Authored guns spawn with a full magazine and
full reserve ammunition; player drops retain their actual remaining ammunition.
The project does not implement FAMAS, Galil,
MP5 Navy or XM1014; their 20 authored pickups are omitted, not substituted.
Source directional bump lightmaps and cubemap reflections are not reproduced.

Validation (Godot Mono 4.8 beta 1, always `--audio-driver Dummy`):

- `dotnet build --nologo`: zero errors and warnings.
- `--headless --fixed-fps 66 scenes/debug/arena_2000_checks.tscn -- --menu-test-profile`:
  initial spawn, all 32 spawn floors, repeated team respawns, actual overlap
  pickup for each supported weapon type, and knife hits destroying all nine glass panels.
- `--headless --fixed-fps 66 scenes/debug/bot_checks.tscn -- --bot-map=2000 --menu-test-profile`:
  25 checks, including pathfinding, movement, combat, visibility, death and respawn.
- `--headless --fixed-fps 66 scenes/debug/menu_checks.tscn -- --menu-test-profile`:
  map discovery, thumbnails, mode filtering and scene loading.
- `--script tools/source_import/render_2000.gd`: overview, both spawn sides and
  a lower corridor, visually inspected; overview supplies the real menu thumbnail.

Rebuild with the existing BSP pipeline using `--map 2000 --bsp <original path>`.
Then run `build_deathmatch_spawns.gd -- 2000`, `build_bot_navigation.gd -- 2000`
and the render script if geometry changes.
