# Solo presentation and Mobile materials, 2026-10-19

The local body now uses the same visibility-based pose evaluation as remote
players. First person keeps animation clocks but skips hidden skeleton writes;
hitboxes, weapon attachments and death capture request current bones. Switching
to third person requests a fresh pose immediately.

Knife inertialization stops restoring and rewriting the entire skeleton after
its residual transition reaches zero. Authored animation and velocity tracking
continue, including when another inspect or attack interrupts the animation.

Mobile's default RGB10A2 path halves output luminance. With `blend_mul`, neutral
white consequently became a darkening factor. Mobile-only output compensation
fixes bullet holes, knife scratches, map modulation decals and multiplicative
explosion particles. The compensation targets the project's existing non-HDR-2D
viewports; enabling HDR 2D requires revisiting it. Forward+ output is unchanged.

Validation:

- Main and isolated C# builds: zero warnings/errors.
- Rendered visibility, local hitboxes/death and camera switching: 24 checks each
  on Forward+ and Mobile, zero failures.
- Player presentation, headless: 263 checks, zero failures.
- Knife styles/actions/inspect interruptions: 3,151 checks, zero failures.
- Knife poses compared with the previous implementation at 30/60/144/360 FPS:
  1,012,176 comparisons, zero failures; maximum position difference zero.
- Short rendered material regression: five cases per renderer, zero failures
  and zero sampled colour error. Covers neutral borders, fading and refraction.

```text
Godot --path COPY --rendering-method mobile --audio-driver Dummy --windowed --script res://tools/performance/check_mobile_materials.gd
```

The material regression takes a few seconds; repeat with `forward_plus` to check
the other path. `%TEMP%/contrapoint-solo-path.txt` identifies the isolated project,
original knife oracle and raw logs/screenshots. The longer optional
`player_performance.tscn -- --console-test-profile --menu-test-profile --perf-solo OUTPUT`
probe samples karambit, butterfly and an explicitly equipped AK twice. Initial
short FPS samples varied substantially; no reliable FPS percentage is claimed.
