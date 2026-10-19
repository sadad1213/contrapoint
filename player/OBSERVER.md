# Observation

Dead DE players automatically enter first-person observation after the existing
death/freeze camera. Left/right mouse selects the next/previous living teammate;
Space switches between first person and chase. Clicking can skip the freeze
camera. If no teammate is alive, the corpse camera remains until the round ends.
Death and disconnect select a replacement automatically, without revealing an
opponent to a dead team member.

The spectator team starts in free flight. Clicking selects a living player of
either team. Space cycles first person, chase and free flight. Menus and console
take input before observer controls. Following disables the observer's movement,
look and combat; it never enables the selected player's input. Respawn restores
the player's own camera, weapon layer and HUD. Spectators keep their selected
peer and mode through the round's map reload; disconnected peers fall back to an
available target.

`PlayerObserver` follows the rendered eye, pitch/recoil and FOV. The target's
body and held world weapon move to a visibility layer excluded only by the
observer camera; they remain visible to other cameras and retain collision.
Chase uses a world sphere cast. Camera movement for locally simulated players
interpolates physics eye samples; remote eyes follow network presentation.

`ObserverViewmodel` renders a passive model copy in a private SubViewport world.
It samples the selected weapon's animation and camera-space motion without
running its controller or input. Event tracks cannot trigger audio, damage or
weapon actions. Sniper zoom hides the model and uses the existing scope overlay.
The local HUD and radar read `Player.HudPlayer`; health, armor, ammunition, money,
team contacts, objective progress and target name follow the selected player.
Flash opacity/timing and smoke density use that view as well. Player snapshots
carry view angles, zoom, active ammo, flash state and viewmodel motion through
the host to other clients. A replicated flash uses the white overlay; a remote
player's captured retinal afterimage is not transmitted.

Verification after `dotnet build` (Godot Mono, `--audio-driver Dummy`):

```text
--headless --path . scenes/debug/observer_checks.tscn -- --console-test-profile --menu-test-profile
--windowed --path . scenes/debug/observer_checks.tscn -- --console-test-profile --menu-test-profile
```

The rendered run saves rifle/reload and scope previews in `.godot/observer_*.png`.
Also run `hud_checks.tscn`, `death_checks.tscn`, `team_checks.tscn`, and
`tools/run_lan_checks.ps1 -Godot PATH -Map dust2 -Defuse`. The LAN check uses a
host, a playing client and a late spectator, then verifies observer state after
defuse and round reload.
