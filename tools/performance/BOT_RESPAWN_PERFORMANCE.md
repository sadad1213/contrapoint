# Bot respawn and allocation follow-up, 2026-10-19

Deathmatch spawn selection constructed physics queries for every enemy and
candidate, repeatedly read native player transforms, and scored every candidate
fully before sorting. The selector now snapshots positions, reuses two ray
queries, releases native result containers immediately, and stops evaluating
candidates that cannot beat the best complete score. Candidate order, the
32-valid-point limit, safety priorities and tie breaking are preserved.

Movement reuses its water query and stair/floor collision results. These owned
resources are released on deletion, so reparenting a live player remains valid.
Shotgun reloads previously dereferenced a nonexistent body reload clip and
could emit exceptions every frame. They now retain their weapon-specific upper
pose while the real weapon reload timeline continues.

The experimental nearest-first perception scan was removed because measurements
did not establish a consistent benefit. AI reaction and physics tick rates are
unchanged.

## Measurements

Headless Godot 4.8 beta1 Mono, real Dust II collision, expert bots. The added
`--perf-spawns-only` mode measures the actual selector on the local player,
without inventory/model construction: eight warmup calls and 64 measured calls
at each population. It does not measure the complete respawn or frame time.

| Bots | Mean before/after, ms | p95 before/after, ms | Managed bytes before/after |
| ---: | ---: | ---: | ---: |
| 4 | 4.605 / 0.756 | 5.868 / 0.857 | 211,190 / 14,989 |
| 8 | 12.310 / 0.775 | 29.840 / 1.384 | 345,486 / 19,575 |
| 15 | 14.893 / 1.747 | 38.679 / 2.612 | 642,226 / 36,815 |

At 15 bots, this operation's mean fell about 88% and managed allocation about
94%. Earlier optimized samples were faster; the table uses the final repeat.

Controller-only timings remain variable. At 15 bots, two baseline runs averaged
1.823 and 1.984 ms; the final run averaged 2.551 ms. Managed allocation fell from
9,160–9,384 to 7,908 bytes/tick. Different encounters/path counts and scheduling
prevent a reliable general CPU/FPS improvement claim. Further profiling of
steady-state bot load remains useful.

The optional rendered benchmark was stopped when the user requested completion.
Its partial baseline emitted shader-instance-buffer capacity errors, so it is
not used as FPS evidence. Raw completed CPU measurements are in
`bot_respawn_results.json`; the logs directory is recorded there and in
`%TEMP%/contrapoint-bot-opt-path.txt`.

## Validation

- Main and isolated builds: zero warnings/errors.
- Spawn equivalence against the original exhaustive scoring: 193 checks pass,
  covering 0/4/8/15 bots, FFA/TDM, previous-point avoidance and missing floor.
- Actual M3/XM1014/AK reloads across all eight agents and standing/crouching,
  stationary/moving poses: 198 checks pass.
- Final 30-second eight-bot combat soak: 27 checks pass; no runtime exceptions.
- Surf: 875 checks; ladders: 51; damage/water: 52; prop movement: 8;
  offline deathmatch: 10; autonomous planting/defusing: 3. All pass.
- General PlayerChecks still has two input assertions failing, and the full
  BodyPoseChecks weapon-clock comparison fails at Glock tick 665. Both reproduce
  in a separate baseline copy without this patch. The new reload checks can run
  independently with `--reload-poses-only`.

```text
Godot --headless --audio-driver Dummy --path COPY res://scenes/debug/bot_performance.tscn -- --console-test-profile --menu-test-profile --perf-spawns-only
Godot --headless --audio-driver Dummy --path COPY res://scenes/debug/bot_spawn_checks.tscn -- --console-test-profile --menu-test-profile
Godot --headless --audio-driver Dummy --path COPY res://scenes/debug/body_pose_checks.tscn -- --console-test-profile --menu-test-profile --reload-poses-only
```
