# Player snapshot allocation, 2026-10-18

Reports describe freezes on both host and clients that become worse with more
human players. A headless population probe found substantial allocation in the
30 Hz player snapshot path and render-frame body animation. These are plausible
contributors to collection/finalization stalls; the probe does not establish
that every reported in-game freeze has this cause.

Godot's C# string-to-Variant conversion creates a native string plus a managed
disposer registered for finalization. Dictionary lookups implicitly performed
this conversion for every field of every snapshot. The same packet's weapon
name was read again for each inventory candidate. Animation lookups also
repeated implicit string-to-StringName conversions.

Fixed player wire keys now retain their String Variants for the runtime's
lifetime. Temporary string values and captured dictionaries are released
explicitly. The host creates one dictionary Variant per captured player and
shares it across recipients before disposing it, avoiding a native-reference
wrapper for each player/recipient pair. Packet fields and protocol stay the same.

Body and remote weapon animation names are cached per instance. Body caches
reset on model changes; both caches release their owned names on tree exit.
The authored clips, sampling frequency, hitboxes and movement smoothing remain
unchanged. Native allocation behavior was checked against the installed Godot
4.8 beta1 GodotSharp assembly's Variant and Dictionary implementations.

## Measurements

`scenes/debug/multiplayer_performance.tscn` creates 1, 4, 8 and 16 real player
rigs, with scripted movement velocity and local input disabled. Each operation
gets 20 warmup iterations and 180 samples. Capture/apply run separately from
body sampling. Raw values are in `multiplayer_population_results.json`.

| Work for 16 players, per update | Before | After |
| --- | ---: | ---: |
| Capture: managed allocation | 71,436 B | 20,840 B |
| Apply: managed allocation | 59,908 B | 17,282 B |
| Body poses: managed allocation | 14,464 B | 5,248 B |
| Capture: median CPU time | 0.734 ms | 0.436 ms |
| Apply: median CPU time | 2.105 ms | 1.597 ms |

Snapshot allocation fell about 71%; body-pose allocation fell about 64%.
Initial and repeated baseline probes included snapshot preparation/application
outliers around 30–45 ms. Timing maxima are noisy: the final body-pose sample
also had a 35 ms outlier and its median was slower than baseline. Therefore
these numbers support allocation reduction, not elimination of all stalls or
an estimated match FPS improvement. Rendering, map logic, RPC serialization,
transport and actual player inputs are excluded. Managed byte counts also do
not measure the total native allocation avoided.

## Verification and limits

- Isolated final build: zero warnings/errors.
- Player presentation: 263 checks passed.
- Teams/model selection/network state: 83 checks passed.
- Two-process KZ ENet movement: 195 checks passed.
- Full KZ combat/late-join check timed out at the rear knife attack against the
  armored host. Restoring the pre-optimization implementation in the isolated
  copy reproduced that same timeout at host check 196. The remainder of this
  full scenario is unverified.
- A movement run concurrent with the population benchmark failed the initial
  crouch/pitch fixture; repeating it on its own passed. This fixture is sensitive
  to scheduling and was not changed as part of this optimization.

After the user's request to leave the live server alone, all builds and runs
used a separate project copy, test profiles and UDP 27235–27237. No connection
to or restart of the user's server was performed. Two earlier builds had
already written the main project's build output before that request; later
source changes were compiled only in the isolated copy. The main project
needs a fresh build when the active session is finished.

`%TEMP%/contrapoint-stutter-path.txt` records the isolated copy and raw logs.
The population benchmark can be repeated after building the intended copy:

```text
Godot --headless --audio-driver Dummy --path COPY res://scenes/debug/multiplayer_performance.tscn -- --console-test-profile --menu-test-profile
```
