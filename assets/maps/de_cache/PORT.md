# de_cache

Source: `C:/Users/Hueglot/Downloads/de_cache_2/de_cache.bsp` (2011 Source BSP).
Launch from the menu as CACHE, or use `map de_cache` in the console.
Playable wrapper: `res://scenes/maps/de_cache.tscn`.

The shared catalog discovers this wrapper for menu, console and LAN. The menu
thumbnail is a rendered view of site A. Native scene contains 219,816 triangles,
273 static props, 52 physics props, 1,302 world collision brushes, 173 player
clip hulls, two ladders, 15 light glows and 14 original spawns per team.
There are 251 generated reachable deathmatch spawns and baked bot navigation.
The supplied Source `.nav` is not used; navigation is baked against Godot collision.

## Bomb mode integration

Both original `func_bomb_target` entities are native `Area3D` nodes in the
persistent `source_bomb_target` group, with collision layer 8 and mask 1.
Their `source_data` dictionaries retain classname, Hammer ID and authored hulls.
They are present in every game mode; no placeholder replacement is used.

| Site | Node under Cache | Hammer ID | World-space bounds in Godot metres |
| --- | --- | --- | --- |
| A | `brush_10` | `492263` | x -10.0838..-2.0320, y -2.1082..-1.2954, z -51.6128..-37.5158 |
| B | `brush_11` | `583911` | x -7.8994..5.4864, y -2.8194..-2.0066, z 26.7208..37.2872 |

These are shallow authored volumes. Use player hull overlap (or the existing
`overlaps_current_position` query), rather than requiring the player's foot
point to lie inside the volume. Physical player entry and exit at both sites
are covered by `scenes/debug/cache_checks.tscn`.

Original buy zones are also preserved in `source_buyzone`. Team spawns retain
their authored rotations. The A door is `brush_37` / targetname `A_Door`;
Use opens it and it closes after the original eight-second wait. Breakables
are `brush_12`, `brush_13` (vents) and `brush_31` (glass).

## Verification and limits

Run map-specific checks with the Mono Godot executable:

```text
Godot --headless --audio-driver Dummy --path . --fixed-fps 66 scenes/debug/cache_checks.tscn
python tools/source_import/verify_bots.py --godot PATH_TO_MONO_GODOT --map de_cache
Godot --audio-driver Dummy --path . --script tools/source_import/render_cache.gd
```

Screenshots under `source/` cover both sites, T/CT spawns and mid.
Map checks exercise original spawns and respawns after physics ticks, actual
Use on the A door, actual knife traces against all three breakables, physical
entry/exit in both bomb zones, ladder movement and physics prop impulses.

Remaining visual omissions: smokestack smoke, spark and dust-mote particles;
decals on displaced surfaces; Source directional bump lighting and cubemap
reflections. Bomb round rules are implemented separately by the DE mode work;
this port supplies its original objective volumes and team infrastructure.
