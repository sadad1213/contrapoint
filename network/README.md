# LAN

Load a map and enter `lan 1` on the host. The console prints each active IPv4
address with port 27015, including virtual interfaces, without adapter labels.
`lan 0` closes the server and disconnects clients; the host keeps playing locally.
`lan` displays the current state. Commands are case insensitive.

On another copy of the project, use `connect 192.168.0.184` (substitute the
host's address) or `connect 26.x.x.x:27015`. The command also works in the main
menu. The client loads the server's map and receives its existing world state
before gameplay starts. `disconnect` returns to the menu. All peers need the
same protocol, map scene, entity scripts and map metadata. UDP port 27015 must
be reachable. The session supports a host and up to 15 clients.

Changing the map through the menu or `map` ends the session; open LAN again on
the new map. Menus release local input without pausing a network session.
Deathmatch time-limit and `mp_restartgame` reloads keep connections, teams,
models and selected weapons; see [deathmatch rules](../gameplay/README.md#deathmatch).

## Ownership and replication

* ENet with a persistent `/root/LanSession` RPC endpoint. Reliable messages
  handle joining, actions, effects, teleports and disconnects. Player poses and
  projectiles update at 30 Hz. Map entities update at 10 Hz; unchanged states
  are refreshed once a second to recover dropped packets. Packets are split by
  serialized size, with oversized entities sent reliably.
  The host captures and encodes each world/player snapshot once per update,
  sharing it across recipients. Animation/audio lookups are cached and refreshed
  when those nodes enter or leave the tree. Clients interpolate only moving
  entities, removing settled/deleted objects from the render-frame work list.
* Each player simulates its own movement immediately. The host relays poses,
  velocity, crouch, grounded state, walking, aim pitch, agent, health, weapon,
  silencer and sampled action animation. Both host and clients smooth remote
  movement. The host smooths the visible body while keeping collision, hitboxes,
  gameplay eyes and relayed poses at the latest received coordinates.
  This LAN implementation trusts player movement; it does not yet implement
  server movement validation, prediction reconciliation or lag compensation.
* The host owns rigid bodies, prop impulses, doors, rotating brushes, trains,
  buttons, glass, entity outputs, timers and trigger teleports. Clients stop
  those simulations and apply snapshots identified by map-relative paths.
  Deletions remain in snapshots as tombstones, including for late joiners.
  Static geometry and resources load from the same local map assets.
* Use, shots, melee and grenade throws are sent to the host. It resolves world
  damage and grenade physics; tracers, impact marks, action sounds, footsteps,
  grenade spin, bounces, flashes and smoke replicate to other peers. Late
  joiners receive active grenades and smoke with their current age. Players take
  damage from falls, HE, bullets and knives, both offline and in LAN. The host
  owns health, armor and death; reliable messages also update the owning client.
  Landing speed is reported by the movement owner, consistent with the existing
  trusted movement model. Respawns are requested from the host. Client pose
  snapshots cannot overwrite health or armor. Bullets use the original MDL bone
  hitboxes, weapon CTX damage, range loss, armor ratio and penetration. Knife
  damage uses the solid hull and the victim's facing for backstabs. Damage
  feedback (aim punch, tagging, blood, helmet sparks, hit/death sounds and HE
  hearing shock) is sent reliably to all peers, including the victim and shooter.
  Cosmetic impact messages never apply damage. Damage cvars are host-owned,
  replicated on join/change, and restored to the client's local values on leave.
  Reliable death context carries the model pose, inherited velocity, hit position,
  hitgroup and impulse. Each peer simulates its cosmetic ragdoll locally. Held
  world weapons are removed on death until dropped weapons are implemented.
  Manual and automatic respawns update every peer and advance the teleport
  revision. Freeze-camera and automatic respawn rules are replicated/restored
  alongside damage rules. Protocol is now 9. DM rules, time, scores and measured
  RTT replicate reliably; magazine rewards go only to the owning client and
  are bounded by map generation and respawn revision. Map reload handshakes
  preserve ENet peers and reject packets from earlier map generations.
* Dust II DM bots are server-simulated players with negative roster IDs. Their
  names, weapons, movement, scores, impacts, sounds, deaths and respawns use the
  existing replication. Clients run presentation only. Bot slots count toward
  the shared 16-player quota; ping displays `BOT` rather than querying ENet.
* Kill-feed notices are separate reliable host events, carrying the names,
  teams, weapon and actual shot conditions. Health snapshots, corpse late-join
  replay and respawns do not add another notice. The shooter captures scope
  state before the sniper rifle releases the scope for the bolt cycle; the
  scope returns to the same magnification once the weapon is ready again. The
  host resolves penetration and smoke intersections and checks the shooter's
  flash/grounded state.
* Server teleports increment a revision so queued old poses cannot undo them.
  Player collisions are disabled between network peers for this testing stage.

New interactive map scripts need registration in `CollectWorld` and an explicit
list of serializable properties in `WorldProperties`. New runtime entity types
need a spawn/despawn protocol, as projectiles and smoke already have. Cosmetic
camera-facing beams and soundscapes remain local.

## Verification

Build the C# project, then launch a real three-process ENet integration check:

```powershell
dotnet build --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run_lan_checks.ps1 -Godot 'C:/path/to/Godot_mono_console.exe' -Map inferno
```

Supported maps: `dust2`, `inferno`, `kz_jayverb000`. The check uses an isolated
run directory and console test profile. It exercises menu `connect`, map loading,
initial and live prop state, players and held weapons, action animation, host
shot resolution, enemy bullet/armor feedback, armored knife backstab/death,
host-only damage cvars, smoke, late join, authoritative teleport,
peer departure, server close and reopening the UDP port. Inferno additionally
checks open/close doors, broken glass and deleted map entities.

The runner defaults to UDP 27016 to avoid an installed CSS server on 27015;
use `-Port 27185` to select another isolated test port. Normal gameplay still
defaults to 27015. `--lan-port=N` changes the port for that process.

Add `-Movement` for a focused two-process movement check. It checks host model
continuity between real ENet packets, render-frame position and yaw smoothing,
authoritative collision/eyes/hitboxes, relayed coordinates and large corrections.

Add `-Cheats` for a focused two-process check of client `god` and `ent_create`,
server-side invulnerability, weapon replication and the `sv_cheats 0` reset.

Add `-World` for a three-process check of initial/live world state and late join
without the combat fixtures. On Inferno this includes props, doors, broken glass
and deleted entities. `scenes/debug/lan_world_checks.tscn` separately checks
snapshot bytes, dynamic animation/audio cache invalidation, smoothing convergence,
stale packets, large corrections, deletions and cleanup.

Source references consulted: CS:S
[player send tables and animation events](https://github.com/nillerusr/source-engine/blob/master/game/server/cstrike/cs_player.cpp),
[player animation state](https://github.com/nillerusr/source-engine/blob/master/game/shared/cstrike/cs_playeranimstate.cpp),
and Godot's [high-level multiplayer API](https://docs.godotengine.org/en/stable/tutorials/networking/high_level_multiplayer.html).
