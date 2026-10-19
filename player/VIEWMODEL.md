# CSSO viewmodel bob and sway

Console controls (available without `sv_cheats`):

```text
cl_bob_style 0    // CS:S bob
cl_bob_style 1    // CS:GO bob, default
toggle cl_bob_style
bind F7 "toggle cl_bob_style"
```

Both styles use CSSO angle-history sway. The style, cycle, amplitudes and sway
settings are archived through the existing console config system and survive
respawn, map changes and restarts.

| Command | Default | Meaning |
| --- | --- | --- |
| `cl_bobcycle` | 0.98 | CS:GO cycle multiplier; also depends on weapon movement speed |
| `cl_bobcycle_old` | 0.8 | CS:S cycle duration in seconds |
| `cl_bobamt_vert` | 0.25 | CS:GO vertical amplitude |
| `cl_bobamt_lat` | 0.4 | CS:GO lateral amplitude |
| `cl_bob_lower_amt` | 13 | CS:GO running lowering |
| `cl_viewmodel_shift_left_amt` | 1.5 | CS:GO shift as accuracy improves |
| `cl_viewmodel_shift_right_amt` | 0.75 | CS:GO shift as accuracy worsens |
| `cl_wpn_sway_scale` | 1.6 | Sway strength; 0 disables it |
| `cl_wpn_sway_interp` | 0.1 | Sway delay in seconds; 0 disables it |

The existing `cl_bob` master amplitude and `cl_bobup` wave shape retain their
cheat protection. Setting `cl_bob 0` disables bob, including lowering, but keeps
independent sway. Zero amplitudes/shift settings are allowed for customization.

## Sources and limits

Behaviour is adapted from the author's public **CSSO 0.10** source, commit
`845aa5c9610f959d850f41310535ec88baaa4f3e`:

- [weapon_csbase.cpp](https://github.com/NOMFPS/csso-src-9/blob/845aa5c9610f959d850f41310535ec88baaa4f3e/src/game/shared/cstrike/weapon_csbase.cpp):
  `CalcViewModelBobHelper`, `AddViewModelBobHelper`, legacy
  `CalcViewmodelBob`/`AddViewmodelBob`.
- [predicted_viewmodel.cpp](https://github.com/NOMFPS/csso-src-9/blob/845aa5c9610f959d850f41310535ec88baaa4f3e/src/game/shared/predicted_viewmodel.cpp):
  `CalcViewModelLag` and airborne pitch/lowering.

The installed `csso/cfg/config.cfg` supplies the current spelling
`cl_bob_style`, modern default 1, amplitudes .25/.4, lowering 18
and shifts 1.5/.75. The installed `csso/bin/win64/client.dll` also contains the
current switch, `cl_bobcycle_old`, sway commands and the `1.6` scale string.
These checks do not establish exact parity with the closed CSSO 2.0 client.

Contrapoint defaults to lowering 13 by user preference; the installed CSSO value
is 18. Previously saved configs retain their explicitly stored value.

Godot adaptations:

- Movement and amplitudes use Source units converted by `SourceMovement.Unit`.
  Bob speed is limited to 320 with 320/s legacy or 640/s modern smoothing.
  Modern airborne amplitude is reduced and running lowers the model.
- Bob uses one gait clock per player. Weapon changes preserve that clock and
  sway history; respawn/teleport clear both. Accuracy displacement starts from
  zero on a weapon change rather than retaining a separate value per weapon.
- The lateral wave uses a regular double-length period, retaining the project's
  existing corrected modulo rather than the malformed integer expression in
  the published source. The ignored return from CSSO's running-factor `clamp`
  is preserved; the final vertical result is clamped to -7..4.
- Accuracy shift uses the project's CSSO accuracy profiles and exponential
  smoothing. Air lowering approaches 2 degrees at 6 degrees/s (the source's
  .1-per-frame approach expressed at 60 Hz). The source's landing/view-height
  helper is not added here; the project's movement/camera already handles landings.
- Sway interpolates pitch/yaw history at the configured delay, handles yaw wrap,
  fades near vertical aim and offsets position without adding artificial roll.
  Mouse sensitivity and keyboard turning affect it through actual view angles.
  A free cursor keeps history advancing without generating camera movement.
- `WeaponSway` applies the shared transform before imported animation. Standalone
  weapon previews use the same sway formula against their viewport camera.

## Verification

```powershell
dotnet build --no-restore
& $Godot --headless --path . --script res://tools/source_import/verify_viewmodel_bob.gd -- --console-test-profile --menu-test-profile
& $Godot --headless --path . res://scenes/debug/viewmodel_motion_checks.tscn -- --console-test-profile --menu-test-profile
& $Godot --headless --path . res://scenes/debug/console_checks.tscn -- --console-test-profile --menu-test-profile
```

The motion scene checks actual player/weapon transforms, console switching and
config round trips, bounds and settling at 30/60/144 FPS, plus numerical sway
direction/delay checks at 30/60/144/300 FPS. The console scene checks real mouse
look, free-cursor settling and console input isolation. Test profiles keep the
checks separate from saved gameplay preferences.
