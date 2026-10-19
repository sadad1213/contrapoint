# Player animation and whole-frame FPS, 2026-10-19

The earlier bot probe measured controllers without body animation or rendering.
Rendered Mirage (`de_cpl_strike`) reproduced a much larger population cost:
about 284 FPS alone, 163 with five bots and 116 with ten. Most bots were outside
the camera; map draw calls remained at 125. Disabling all body poses in the
initial five-bot diagnostic raised FPS from roughly 185 to 254 with AI already
paused. Disabling weapon animation also recovered CPU time.

## Changes

- Body animation samples only the position/rotation tracks belonging to each
  layer. It composes the desired pose in managed arrays and writes the final
  result once. Previously two complete AnimationPlayer seeks wrote both layers
  into the skeleton, followed by saving, restoring, reading and rewriting bones.
  Track maps and bone parents are cached; unchanged final transforms avoid
  redundant native setters. Authored clips and visible sampling frequency stay
  the same.
- Non-local rigs use a VisibleOnScreenNotifier3D with a generous body box.
  Offscreen/occluded actors advance their gait, action and recoil clocks without
  sampling and writing the skeleton. Entering the screen, hitbox queries, muzzle
  attachments, weapon drops and death capture request an up-to-date pose.
  Requests share the current pose until animation or physics advances. Headless
  runs continue regular full pose updates.
- A conservative four-metre bound rejects unrelated bullet segments before
  requesting hidden bones. The pose checks verify hitbox corners against that
  bound for every imported agent/clip at four sample times.
- Server bot firearms advance their animation timeline without evaluating an
  invisible first-person skeleton. Action completion still handles reloads and
  silencers; sounds, ammunition, shot cooldowns and world animation clocks run.
  Incoming remote weapon snapshots also seek without sampling hidden viewmodels.
- Non-local HUDs, viewmodel cameras, viewmodel bob and sway no longer update.
  Flash timers continue advancing while their screen overlays are skipped.

## Source references

Valve's [Source SDK SetupBones](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/game/client/c_baseanimating.cpp#L2796)
uses requested bone masks and cached bone data. Its
[animation-event processing](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/game/client/c_baseanimating.cpp#L3549)
also skips invisible presentation effects. The CS:S
[UpdateClientSideAnimation implementation](https://github.com/nillerusr/source-engine/blob/master/game/client/cstrike/c_cs_player.cpp#L1718)
advances animation state separately. That CS:S link is an unofficial source
mirror, whereas the SDK link is Valve's published repository. These inform the
separation of clocks and requested poses; the Godot implementation is adapted
to this project's render layers and gameplay hitbox code.

## Rendered measurements

1920 x 1080, GTX 1060 6 GB, D3D12, Forward+, same Mirage spawn camera, uncapped,
editor open. Each case warms up for 90 frames and samples 240 frames. This is
whole-frame wall-clock FPS with real rendering and bot physics. The quiet live
cases disable shooting. The ten-bot combat case enables shooting and keeps
players invulnerable to avoid respawn/loadout creation in the comparison.

| Case | Before FPS | Final FPS |
| --- | ---: | ---: |
| Solo | 284 | 287 |
| 2 live bots | 225 | 253 |
| 5 live bots | 163 | 201 |
| 10 live bots | 116 | 153 |
| 10 bots shooting | 94 | 135 |
| 5 live bots, repeated | 173 | 223 |
| 5 visible animated rigs, AI paused | 177 | 184 |
| Same 5 rigs behind camera, AI paused | 186 | 199 |

The final five-bot live samples improve roughly 23–29%; ten quiet bots improve
31%. Visible-rig-only improvement is small in this camera, so the optimization
does not establish the same benefit when all players are visible. Earlier
intermediate builds measured up to 238–240 FPS for five live bots; those are not
the final-build figures above. Solo performance is effectively unchanged.

AI encounters differ between wall-clock sampling windows, CPU scheduling and
GPU clocks vary, and these are short samples. They are not match averages or a
promise to turn the reported 70 FPS into a specific frame rate. Source assets,
resolution, MSAA and material quality were retained. Before/after crowd images
were inspected; their animation phases differ.

Raw final and baseline rows are in `game_population_results.json`.
`%TEMP%/contrapoint-game-perf-path.txt` identifies the isolated build and raw logs.
Switching baseline/final sources requires `dotnet build --no-incremental`:
copying old source timestamps can otherwise leave the previous assembly in use.

## Repeat

Verification completed in the isolated project copy:

- Pose/track equivalence, sampled hitbox bounds and six weapon timelines:
  1,209,953 assertions, zero failures.
- Rendered visibility, re-entry, lazy hitboxes, distant misses and hidden death:
  15 checks, zero failures.
- Player presentation/cameras: 263 checks, zero failures.
- Rendered HUD: 32 checks, zero failures. Disabling the remote HUD now also
  initializes its status control as hidden before processing is disabled.
- Mirage rendered 30-second combat soak with eight bots: 27 checks, zero failures.
- Mirage two-process LAN: 28 host and 4 client checks, zero failures.
- Isolated and main project builds: zero warnings/errors. The main build was
  repeated successfully after concurrently added buy-menu wiring became complete.

```text
Godot --path COPY --audio-driver Dummy --disable-vsync --windowed res://scenes/debug/game_performance.tscn -- --console-test-profile --menu-test-profile --perf-population --lan-port=27954 OUTPUT_DIRECTORY
Godot --headless --path COPY --audio-driver Dummy res://scenes/debug/body_pose_checks.tscn -- --console-test-profile --menu-test-profile
Godot --path COPY --audio-driver Dummy --windowed res://scenes/debug/body_visibility_checks.tscn -- --console-test-profile --menu-test-profile
```

Without `--perf-population`, the diagnostic separately disables brains, poses,
weapon animation, weapon rendering, HUD and the map to isolate frame costs.
