extends SceneTree

var checks := 0
var failures := 0

func _initialize() -> void:
 call_deferred('verify')

func check(condition: bool, description: String) -> void:
 checks += 1
 if not condition:
  failures += 1
  push_error(description)

func step(weapon: Node, seconds: float) -> void:
 while seconds > 0.000001:
  var dt := minf(seconds, 1.0 / 144)
  weapon.call('_Process', dt)
  seconds -= dt

func mouse(weapon: Node, button: MouseButton, pressed := true) -> void:
 var event := InputEventMouseButton.new()
 event.button_index = button
 event.pressed = pressed
 weapon.call('_UnhandledInput', event)

func active_voices(sound: Node) -> Array[AudioStreamPlayer]:
 var result: Array[AudioStreamPlayer] = []
 for child in sound.get_children():
  if child is AudioStreamPlayer and child.playing:
   result.append(child)
 return result

func verify() -> void:
 AudioServer.set_bus_mute(0, true)
 var bank: Dictionary = JSON.parse_string(FileAccess.get_file_as_string('res://assets/audio/css/bank.json'))
 for definition: Dictionary in bank.sounds.values():
  for path: String in definition.paths:
   var stream := load('res://assets/audio/css/' + path) as AudioStream
   check(stream != null and stream.get_length() > 0, 'Audio must decode: ' + path)
 var preview = load('res://scenes/debug/weapon_preview.tscn').instantiate()
 root.add_child(preview)
 var weapons = preview.get_node('Camera3D/Weapons')
 for weapon in weapons.get_children():
  weapon.set_process(false)
  weapon.call('Holster')
 for name in ['AK47', 'Deagle', 'USP', 'M4A1', 'AWP', 'Scout']:
  var weapon: Node = weapons.get_node(NodePath(name))
  var sounds = weapon.get('Sounds')
  weapon.call('Equip')
  step(weapon, 5)
  var before: int = sounds.get('SoundsPlayed')
  sounds.call('StopAll')
  mouse(weapon, MOUSE_BUTTON_LEFT)
  mouse(weapon, MOUSE_BUTTON_LEFT, false)
  var shot_voices := active_voices(sounds)
  weapon.call('Holster')
  check(not shot_voices.is_empty(), name + ' shot must start a playback')
  for voice in shot_voices:
   check(voice.playing, name + ' holster must let the shot finish')
  weapon.call('Equip')
  step(weapon, 5)
  before = sounds.get('SoundsPlayed')
  mouse(weapon, MOUSE_BUTTON_LEFT)
  mouse(weapon, MOUSE_BUTTON_LEFT, false)
  check(sounds.get('SoundsPlayed') == before + 1, name + ' must emit one accepted shot')
  check(sounds.get('LastSound') == 'weapon_' + name.to_lower() + '.single', name + ' must use the original unsilenced sound')
  mouse(weapon, MOUSE_BUTTON_LEFT)
  mouse(weapon, MOUSE_BUTTON_LEFT, false)
  check(sounds.get('SoundsPlayed') == before + 1, name + ' rejected shot must remain silent')
  step(weapon, 3)
  var reload := InputEventKey.new()
  reload.physical_keycode = KEY_R
  reload.pressed = true
  weapon.call('_UnhandledInput', reload)
  step(weapon, 0.8)
  check(sounds.get('SoundsPlayed') > before + 1, name + ' reload must emit original MDL events')
  weapon.call('Holster')
  before = sounds.get('SoundsPlayed')
  step(weapon, 5)
  check(sounds.get('SoundsPlayed') == before, name + ' holster must cancel pending sounds')
  if name in ['USP', 'M4A1']:
   weapon.call('Equip')
   step(weapon, 5)
   mouse(weapon, MOUSE_BUTTON_RIGHT)
   step(weapon, 5)
   check(weapon.get('Silenced'), name + ' silencer must attach')
   mouse(weapon, MOUSE_BUTTON_LEFT)
   mouse(weapon, MOUSE_BUTTON_LEFT, false)
   var expected := 'weapon_usp.silencedshot' if name == 'USP' else 'weapon_m4a1.silenced'
   check(sounds.get('LastSound') == expected, name + ' must use its silenced sound')
   weapon.call('Holster')
 # Check exact AK event boundary and frame skipping without replaying events.
 var ak = weapons.get_node('AK47')
 ak.call('Equip')
 step(ak, 2)
 var sound = ak.get('Sounds')
 var count: int = sound.get('SoundsPlayed')
 sound.call('StartAnimation', 'ak47', 'ak47_reload', 2.4)
 sound.call('AdvanceAnimation', 0.34)
 check(sound.get('SoundsPlayed') == count, 'AK clipout must not occur before its MDL cycle')
 sound.call('AdvanceAnimation', 1.6)
 check(sound.get('SoundsPlayed') == count + 2, 'Skipping frames must preserve both crossed reload events')
 sound.call('AdvanceAnimation', 1.6)
 check(sound.get('SoundsPlayed') == count + 2, 'Sampling the same position must not replay sounds')
 sound.call('StartAnimation', 'ak47', 'ak47_reload', 2.4)
 sound.call('CancelAnimation')
 sound.call('AdvanceAnimation', 9)
 check(sound.get('SoundsPlayed') == count + 2, 'Cancelled animation must remain silent')
 sound.call('StopAll')
 ak.call('Equip')
 step(ak, 0.4)
 var draw_voices := active_voices(sound)
 ak.call('Holster')
 check(not draw_voices.is_empty(), 'AK draw must start its bolt sound')
 for voice in draw_voices:
  check(voice.playing, 'Draw sound must finish after holstering')
 # More than the initial pool size must overlap without replacing any tail.
 sound.call('StopAll')
 for shot in 12:
  sound.call('PlayScript', 'weapon_ak47.single', 1.0, true)
 check(active_voices(sound).size() == 12, 'Overlapping sounds must not steal playing voices')
 # Dummy audio doesn't mix. Request WASAPI explicitly with -- --require-mix
 # for this optional real-output smoke check (one audible shot).
 if '--require-mix' in OS.get_cmdline_user_args():
  for weapon in weapons.get_children():
   weapon.get('Sounds').call('StopAll')
  var capture := AudioEffectCapture.new()
  var effect_index := AudioServer.get_bus_effect_count(0)
  AudioServer.add_bus_effect(0, capture)
  AudioServer.set_bus_mute(0, false)
  sound.call('PlayScript', 'weapon_ak47.single', 1.0, true)
  await create_timer(0.3).timeout
  var frames := capture.get_buffer(capture.get_frames_available())
  var peak := 0.0
  for frame in frames:
   peak = maxf(peak, maxf(absf(frame.x), absf(frame.y)))
  check(peak > 0.001, 'CSS gun audio must produce a non-silent audio mix')
  print('CSS_AUDIO_MIX frames=', frames.size(), ' peak=', peak)
  AudioServer.remove_bus_effect(0, effect_index)
 AudioServer.set_bus_mute(0, false)
 preview.free()
 OS.delay_msec(150)
 # Let the audio server release stopped playbacks before the test exits.
 await create_timer(0.1).timeout
 print('CSS_AUDIO checks=', checks, ' failures=', failures)
 quit(0 if failures == 0 else 1)
