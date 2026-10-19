# Bot CPU cost, 2026-10-19

Brains repeatedly enumerated scene groups and rebuilt native player exclusion
arrays for visibility rays. A perception update could trace the same target
three times. Floor probes also constructed new queries and exclusion arrays
every movement tick.

Patrol selection searched for a reachable destination, discarded that route,
then searched again. All routes were rebuilt every second, including stationary
firearm combat. If patrol selection failed, the empty-path condition restarted
up to twelve searches every physics tick, bypassing the normal retry timer.

The manager now shares player/smoke snapshots within a physics tick and retains
the sight exclusion array until the roster changes. Controllers reuse ray
queries, release ray results immediately, and reuse target visibility within a
perception update. Shooting still checks current visibility before firing.

Patrol selection returns its existing route. Static patrol routes last until
arrival or stuck recovery; failed patrol selection waits one second before
retrying. Combat without knife pursuit skips navigation while aiming. Losing
sight, investigating a sound, moving the target goal and stuck recovery still
invalidate navigation as needed.

## Measurements

`scenes/debug/bot_performance.tscn` runs on real Dust II collision/navigation,
with 4, 8 and 15 server bots, expert profiles, and shooting disabled. For each
population it warms up for 66 physics ticks and samples another 660. It measures
controller work, including perception, navigation, movement and floor probes.
Body animation is disabled for the probe, and rendering, automatic weapon
updates and other scene callbacks are outside the measured interval.

| Bots | Mean before/after, ms | p95 before/after, ms | Managed bytes/tick before/after |
| ---: | ---: | ---: | ---: |
| 4 | 1.379 / 1.124 | 3.056 / 1.880 | 5,563 / 2,633 |
| 8 | 1.868 / 1.315 | 3.376 / 1.983 | 10,772 / 4,773 |
| 15 | 6.342 / 2.046 | 15.495 / 2.937 | 25,862 / 9,827 |

At 15 bots the mean controller time fell about 68%, p95 fell 81%, and managed
allocation fell 62%. Successful route builds over the cumulative population run
fell from 310 to 45. Both runs use the same seed, but changed path scheduling
changes random consumption and subsequent movement/combat encounters. These are
workload measurements, not identical trajectories or a rendered match FPS claim.
Timing outliers remain; the 4-bot maximum increased from 29 to 50 ms.

Raw measurements are in `bot_population_results.json`. Builds and runtime checks
use a separate project copy; `%TEMP%/contrapoint-bots-path.txt` records its path
and logs.

## Verification

- Build: zero warnings/errors.
- Offline checks with a 30-second, 8-bot combat soak: 27 checks, zero failures.
- Two-process LAN checks: 28 host checks and 4 client checks, zero failures.
- Added visibility checks for intervening player hulls, active smoke, and smoke
  removal. Existing checks cover movement, shooting/damage/ammunition, teams,
  death/respawn, console population changes and map restart.
- The combat fixture now respawns the shooter to clear earlier patrol memory
  and supplies a sound at the target before resuming AI. This prevents the bot
  from turning toward an unrelated patrol waypoint during its reaction delay;
  that caused the initial LAN attempts to time out before connecting a client.

## Repeat

```text
Godot --headless --audio-driver Dummy --path COPY res://scenes/debug/bot_performance.tscn -- --console-test-profile --menu-test-profile --lan-port=27953
```
