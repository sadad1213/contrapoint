# de_winter_dust2

Launch using `map de_winter_dust2` or select WINTER DUST2 in the map menu.
Wrapper: `res://scenes/maps/de_winter_dust2.tscn`. The shared catalog also discovers it for LAN.

Source BSP: `C:\Users\Hueglot\Downloads\de_winter_dust2\de_winter_dust2.bsp`.
SHA256: `7b648ef461a82815ce500c7869b38375d570f042d34915e704e5068a51202703`.

Preserved: original geometry, face lightmaps, sky, textures, displacement collision,
compiled convex world collision, original team positions and rotations, bomb/buy
volumes, prop geometry and physics. No missing or substituted materials.
126,198 triangles, 321 static props,
1696 world collision hulls,
62 model physics props.
193 reachable deathmatch spawn markers; 3792 baked navigation polygons.
Navigation is baked from Godot collision; no supplied Source NAV is required.

There are 20 spawns per team, two original bomb targets, two buyzones, 77 displacements and 18 light glows. Winter materials and embedded BSP overrides are preserved.

## Validation

C# build: zero warnings/errors. Offline bot checks: 25 checks passed on each map,
including real movement, shooting, quota/settings and respawn.
Rendered and inspected exterior and interior screenshots in `source/`; the menu
thumbnail is an actual render. Community map checks verify all original team
spawns after physics settles, repeated respawns/rotations, packed triangle counts,
textures, objective entry/exit, physics impulses and every breakable window using
real knife traces. Bomb target, buyzone, prop and ladder counts are cross-checked
against the imported BSP metadata.
Winter Dust II DE bot test passed planting and CT navigation/defusing.

Commands (Mono Godot 4.8 beta1, always Dummy audio):

```text
Godot --headless --audio-driver Dummy --path . --fixed-fps 66 scenes/debug/community_map_checks.tscn -- --menu-test-profile --console-test-profile
python tools/source_import/verify_bots.py --godot PATH_TO_GODOT --map MAP_ID
Godot --headless --audio-driver Dummy --path . scenes/debug/defuse_bot_checks.tscn -- --menu-test-profile --console-test-profile --de-bot-map=MAP_ID
Godot --audio-driver Dummy --path . --script tools/source_import/render_community_maps.gd
```

## Remaining limitations

- Decals on displacement surfaces; Source directional bump lighting and cubemap reflections.
- Godot physics approximates Source VPhysics; brush-box masses use the runtime default scaled by authored massscale.
- Triggerable indoor/outdoor soundscapes, dust/smoke particles and the authored post-bomb fire/explosion entities are not emulated. Ambient fire WAV is extracted and its sound targets are present.
