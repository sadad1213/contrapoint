# Map occlusion, 2026-10-18

The native maps already group geometry by material and 16 m horizontal cells,
but had no occluders and did not enable Godot's occlusion culling. Large numbers
of meshes behind walls were still submitted to the renderer.

`map_occlusion.gd` creates one indexed `ArrayOccluder3D` per map from the actual
opaque static lightmapped surfaces. It excludes entity-owned geometry, props,
alpha-tested materials, transparent materials, water and decals. It neither
simplifies surfaces nor uses collision/player-clip hulls. The existing map meshes,
materials, collisions and gameplay properties are preserved.

The map builder now includes this step. All four existing native `.scn` maps have
also been updated. The weapon viewport disables occlusion explicitly, because
it shares the world while rendering with a separate projection.

## Compare in game

Restart the game to load the updated scenes. Use the console:

```text
fps_max 0
mat_vsync 0
cl_showfps 1
r_occlusion 0
r_occlusion 1
```

Compare while standing still and looking in the same direction. `r_occlusion`
applies to the main viewport and lasts for the session; it is not archived.

## Repeat the native-map benchmark

Use the project's Godot 4.8 beta1 Mono executable with a real graphics driver:

```text
Godot --path . --audio-driver Dummy --disable-vsync --script tools/source_import/benchmark_map.gd -- de_inferno C:/temp/map-bench/de_inferno
```

The benchmark loads the native map without a player, disables map processing,
freezes physics props, uses a 1920 x 1080 viewport, warms up each view for 90 frames
and samples 180 frames. It alternates A/B order. JSON includes average FPS,
median/p95 frame times, draw calls, primitives and renderer CPU/GPU time. PNGs
allow image comparisons. Animated shader time continues to run.

Results are specific to the sampled cameras and are not gameplay FPS averages.
Measurements used the local GTX 1060 6 GB, D3D12 and Forward+, with the editor
still open. Full gameplay adds player, weapon, UI, networking and physics work.
On simple views, occlusion's CPU cost can outweigh the geometry it saves: the
tested KZ bhop view went from approximately 991 to 815 FPS (about +0.22 ms).
Heavy Inferno and Strike views benefited much more.

Occluders in Godot are two-sided. Camera samples inside solid brush geometry are
invalid for visual comparison, and the benchmark skips them using a physics
point query. The old KZ render script's elevated courtyard camera could see
through the back of a solid brush; this benchmark uses an unobstructed position.
Noclip views from inside solids can therefore look different with occlusion.

## Update existing native scenes without rebuilding gameplay

Back up native `.scn` files outside the project before running:

```text
Godot --headless --audio-driver Dummy --path . --script tools/source_import/build_map_occluders.gd -- de_dust2 de_inferno de_cpl_strike kz_jayverb000
Godot --headless --audio-driver Dummy --path . --quit-after 600 --script tools/source_import/verify_map_occlusion.gd -- C:/temp/map-backup de_dust2 de_inferno de_cpl_strike kz_jayverb000
```

The verification recursively compares every stored node/property and embedded
resource with the original scenes, allowing only the added `WorldOccluder` node.
This passed 450,415 comparisons across all four maps.

## Validation and known failures

- `dotnet build --no-restore`: passed without warnings.
- Map builder script parse check: passed.
- Spawn checks: passed, 793 checks across all four maps.
- Console checks after fixing the new toggle's shutdown/save behavior: 111
  checks, zero failures; existing resource cleanup warnings remain.
- Image comparisons across 18 valid camera views: 16 pairs matched exactly;
  two Inferno pairs differed in small animated-water areas.
- Inferno checks fail at the world-brush impact/surface assertion. The same
  failure occurs with the original map backup and occlusion disabled.
- KZ checks fail at a physics teleport assertion (the particular brush varies).
  This also occurs with the original map backup and occlusion disabled.
- `verify_css_map.gd` fails its old player-settling assertion. This also occurs
  with the original Dust II backup; the dedicated spawn checks pass.

Run player/console tests with `-- --console-test-profile` to isolate their console
settings from the user's normal profile. During development of the toggle, its
getter tried to access an already detached movement node at shutdown, and config
saving truncated the normal `user://cfg/config.cfg`. The toggle is now excluded
from archived settings and the save path has been checked again. The normal
config file was left empty; no trustworthy copy of personal overrides was
available. The existing `roundtrip.cfg` contains test bindings and was not used
as a substitute.

Local backups, screenshots and logs from this run are under
`%TEMP%/contrapoint-map-bench`; `backup-path.txt` records the original scene backup
directory. The raw numeric results are preserved beside this document in
`map_benchmark_results.json`.
