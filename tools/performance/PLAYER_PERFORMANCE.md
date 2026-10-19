# Player versus spectator, 2026-10-18

The local GTX 1060 6 GB reproduced the difference at the same Dust II camera:
approximately 184–195 FPS for the player and 319 FPS for the spectator. Live
players add first-person weapon rendering and animation, full-body pose
composition, and the status HUD/radar/crosshair.

The largest avoidable cost in this comparison was rebuilding unchanged HUD draw
commands on every render frame. Radar polygon generation and triangulation, text
measurement and draw submission were repeated even at a stationary camera.

## Changes

- Status panels/text reuse Godot's retained drawing until their snapshot,
  weapon or window size changes. Damage, low-health and clock colour pulses
  continue repainting each frame, including a final repaint after a pulse ends.
- Radar redraws on local movement/rotation, contact changes, radar setting
  changes or window resizing. It keeps the original geometry and colours.
- Crosshair redraws on appearance or size changes and caches its HUD reference.
- Body animation sampling no longer calls `Advance(0)` after an already
  immediate `Seek(..., true)`. Sampling, blend rate and hitbox pose updates keep
  their existing frequency. See the official
  [AnimationPlayer.seek documentation](https://docs.godotengine.org/en/stable/classes/class_animationplayer.html#class-animationplayer-method-seek).

Weapon/HUD resolution, MSAA 4x, materials, font sizes, textures, models and
animation clips are unchanged. Resolution/MSAA/visibility switches in the
benchmark are temporary diagnostic scenarios, not production settings.

## Measurements

1920 x 1080, Forward+, D3D12, same mid camera, native map plus the local player,
knife equipped. The diagnostic disables the player's physics callback to keep
the view exactly fixed, warms up for 90 frames and samples 240 frames per case.
These are stationary rendering comparisons, not average FPS during a match.

| Case | Before | After |
| --- | ---: | ---: |
| Player, first sample | 184 FPS | 271 FPS |
| Player, repeated sample | 195 FPS | 271 FPS |
| Spectator | 319 FPS | 352 FPS |

The player improvement is about 39–47% in these samples. Moving radar contacts
and changing HUD contents still require redraws, so the gain varies in play.
The remaining gap includes the extra weapon viewport and animation work.

Vulkan was also tested without changing the project default: the repeated player
sample reached 296 FPS, compared with 271 on D3D12. The first Vulkan sample was
slower while its cache warmed up. This one-camera result is insufficient to
change the default backend for all maps; `--rendering-driver vulkan` remains
available as an optional launch flag.

## Verification

- Build: zero errors/warnings.
- Rendered HUD checks: 32 checks, zero failures. They verify retained drawing
  while idle and invalidation on armour, movement, settings, resizing and
  low-health animation, plus the existing ammo/visibility checks.
- Player presentation checks: 263 checks, zero failures, including poses,
  strafing, weapon action poses, third-person and interpolated cameras. Run
  without `--fixed-fps`, since that option removes the intermediate render
  fractions these checks require.
- Rendered radar, health readout, clock and crosshair matched the pre-change
  screenshots pixel for pixel. Hands were visually inspected; their animation
  phase differs between captures, while assets, quality and timing are unchanged.
- The normal user's console config hash matched the backup after the checks.
  Diagnostics use `--console-test-profile` and skip normal config saving.

The HUD test fixture was updated to add the Scout explicitly for its shared-ammo
check, because playable loadouts now keep one primary, and to reacquire the AK
after respawn rebuilds the inventory.

## Repeat

```text
Godot --path . --audio-driver Dummy --disable-vsync scenes/debug/player_performance.tscn -- --console-test-profile C:/temp/player-bench
Godot --path . --audio-driver Dummy --windowed --resolution 1280x720 scenes/debug/hud_checks.tscn -- --console-test-profile
Godot --headless --path . --audio-driver Dummy scenes/debug/player_presentation_checks.tscn -- --console-test-profile
```

The diagnostic writes JSON and screenshots into the supplied output directory.
Raw numeric results are saved beside this document. Local screenshots, logs and
pre-change source backups are under the directory recorded in
`%TEMP%/contrapoint-player-bench-path.txt`.
