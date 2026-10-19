# kz_jayverb000

Port of the local Counter-Strike: Source BSP by J & Rvrb. Start from the third
Play card, or enter `map kz_jayverb000` in the developer console. Interact with
the start/finish buttons using **E**. A local timer supplies the functionality
normally provided by a KZ server plugin; starting again resets the current run.
Entering the map and respawning choose a random original T marker in the lobby,
as on Dust II and Inferno. Teleports confirm the player's current overlap so a
stale physics entry at the scene origin cannot bypass the lobby during loading.

The port includes 44 spawns, 39 teleport volumes, nine destinations, 21 sinking
bhop platforms, both buttons, five ladder volumes, the original world collision,
water, static/dynamic model geometry, signs/graffiti, ropes and map audio.
The skybox helicopter follows its eight path tracks. Geometry uses 0.0254 metres
per Source unit. `import_report.json` records the exact BSP path/hash, asset
counts and remaining omissions. No installed game files are modified.

Reproduce extraction with Blender 5.2 and the existing local SourceIO checkout:

```powershell
$blender = 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe'
$sourceio = "$env:TEMP/contrapoint-sourceio/SourceIO"
$game = 'C:/Program Files (x86)/Steam/steamapps/common/Counter-Strike Source/cstrike'
$bsp = 'C:/Users/Hueglot/Downloads/kz_jayverb000/kz_jayverb000.bsp'
& $blender --background --factory-startup --python-exit-code 1 --python tools/source_import/import_css_map.py -- --sourceio $sourceio --game-root $game --map kz_jayverb000 --bsp $bsp --output assets/maps/kz_jayverb000 --interactive
& $blender --background --factory-startup --python-exit-code 1 --python tools/source_import/extract_css_map_gameplay.py -- --sourceio $sourceio --game-root $game --output assets/maps/kz_jayverb000
Godot --headless --audio-driver Dummy --editor --import --path .
Godot --headless --audio-driver Dummy --path . --script tools/source_import/build_css_map.gd -- kz_jayverb000
dotnet build
Godot --headless --audio-driver Dummy --fixed-fps 66 --path . scenes/debug/kz_checks.tscn
Godot --audio-driver Dummy --path . --script tools/source_import/render_kz.gd
```

`render_kz.gd` writes six inspection images under `source/` and creates the menu
thumbnail from the courtyard view. The engine's source import directory is
excluded by `.gdignore`; exported textures and the native `.scn` are standalone.

Decal fixes also apply to Dust II and Inferno. The importer now reconstructs
overlay axes/handedness as encoded by [Valve's BSP compiler](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/utils/vbsp/overlay.cpp).
Clipping handles collinear vertices introduced at BSP T-junctions, keeping the
decal inside its receiving face and its lightmap patch. `--decals-only` refreshes
these meshes while preserving existing world/prop geometry and gameplay data;
it requires the original BSP hash to match. Run
`python tools/source_import/check_decal_geometry.py` for the clipping regression.

Remaining cosmetic differences: Source bump-lightmap directionality/cubemap
reflections, decals on displaced surfaces, spark particles and helicopter rotor
animation. Source server commands are not executed. These limitations do not
prevent the map's teleport, ladder, platform or button interactions.

Verified with the Dummy audio driver: 137 KZ checks (including physical entry
into every teleport), 2,669 Inferno checks, 64 player checks, 75 console checks,
28 menu checks, eight prop-movement checks, and the Dust II scene verifier.
Decal refresh preserved every non-decal geometry chunk and the original atlas
bytes on all three maps. Inspection images cover the spawn, start, courtyard,
bhop section, finish and graffiti.
