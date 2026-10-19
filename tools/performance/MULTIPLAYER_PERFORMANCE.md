# Multiplayer replication, 2026-10-18

For the later population-scaling and temporary allocation investigation, see
[player snapshot allocation](MULTIPLAYER_STUTTER.md).

World replication rebuilt and serialized the same map state separately for
every client. Each capture also traversed entity descendants to find animation
players and copied child lists to find audio sources. On clients, all replicated
entities received a transform assignment every render frame, including static
geometry and objects that had already reached their target.

The host now captures world and player states once per broadcast update.
Animation paths and audio sources are cached, with invalidation on node addition
and removal. Per-peer change filtering, refreshes, packet sizes and sequence
numbers retain their previous behavior. Initial joins capture fresh state.
Client interpolation keeps an active list and stops assigning transforms after
convergence; a later movement packet reactivates the entity.

Remote duck snapshots also avoid recreating an unchanged collision shape.
Weapon animation lookup uses its bound player, and network sampling no longer
evaluates the animation twice after an immediate seek.

## Measurements

Headless Godot 4.8 beta1 Mono, KZ Jayverb000, 117 replicated entities. Timings are
CPU preparation work, excluding RPC transmission, graphics and actual match FPS.
The probe repeated map capture four times per recipient-count scenario, with
the same loaded map and player fixture. Small timing differences between counts
include JIT/cache warmup and measurement noise.

| Work | Before | After |
| --- | ---: | ---: |
| Prepare world update for 3 recipients | 27.437 ms | 3.803 ms |
| Prepare world update for 7 recipients | 64.737 ms | 4.314 ms |
| Interpolate an unchanged world, per render frame | 0.5049 ms | 0.0027 ms |

These are isolated microbenchmarks, not an estimate of the players' FPS gain.
The active match was left running. Builds and headless checks used a separate
project copy, test profiles and UDP 27195. The original project's build outputs
were not replaced. `%TEMP%/contrapoint-mp-perf-path.txt` records the diagnostic
directory with the probe sources and baseline/final logs.

## Verification

- Isolated C# build: zero warnings/errors.
- World snapshot/cache/interpolation checks: 25 checks, zero failures.
- Three-process Inferno world/late-join check: 204 checks, zero failures.
- Two-process KZ movement check: 195 checks, zero failures.
- Player presentation check: 263 checks, zero failures.

Use `tools/run_lan_checks.ps1 -World -Map inferno` or `-Movement -Map kz_jayverb000`
with the existing required `-Godot` executable argument and an unused `-Port`.
Run the standalone world check with the test profiles:

```text
Godot --headless --path . res://scenes/debug/lan_world_checks.tscn -- --console-test-profile --menu-test-profile
```
