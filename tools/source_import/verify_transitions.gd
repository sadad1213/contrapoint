extends SceneTree

var model: Node
var preview: Node
var player: AnimationPlayer
var skeleton: Skeleton3D
var reference: Node
var reference_player: AnimationPlayer
var reference_skeleton: Skeleton3D
var failures: Array[String] = []
var checks := 0

func _initialize() -> void:
 call_deferred('verify')

func check(condition: bool, description: String) -> void:
 checks += 1
 if not condition:
  failures.append(description)
  push_error(description)

func step(seconds: float) -> void:
 while seconds > 0.000001:
  var dt = minf(seconds, 1.0 / 240.0)
  model.call('_Process', dt)
  seconds -= dt

func press(key: Key = KEY_NONE, mouse: MouseButton = MOUSE_BUTTON_NONE) -> void:
 var before := poses(skeleton)
 if key != KEY_NONE:
  var event := InputEventKey.new()
  event.physical_keycode = key
  event.pressed = true
  root.push_input(event)
 else:
  var event := InputEventMouseButton.new()
  event.button_index = mouse
  event.pressed = true
  root.push_input(event)
 var after := poses(skeleton)
 var max_position := 0.0
 var max_angle := 0.0
 for i in before.size():
  max_position = maxf(max_position, before[i].origin.distance_to(after[i].origin))
  max_angle = maxf(max_angle, before[i].basis.get_rotation_quaternion().normalized().angle_to(after[i].basis.get_rotation_quaternion().normalized()))
 check(max_position < 0.0001 and max_angle < 0.002, 'Input snapped the displayed pose')

func poses(rig: Skeleton3D) -> Array[Transform3D]:
 var result: Array[Transform3D] = []
 for i in rig.get_bone_count():
  result.append(rig.get_bone_pose(i))
 return result

func matches_original(clip: String, time: float) -> void:
 reference_player.stop(true)
 reference_player.play(clip)
 reference_player.seek(time, true)
 var error := 0.0
 for i in skeleton.get_bone_count():
  var actual := skeleton.get_bone_pose(i)
  var original := reference_skeleton.get_bone_pose(i)
  error = maxf(error, actual.origin.distance_to(original.origin))
  error = maxf(error, actual.basis.get_rotation_quaternion().normalized().angle_to(original.basis.get_rotation_quaternion().normalized()))
  check(actual.basis.get_scale().distance_to(original.basis.get_scale()) < 0.0001, 'Bone scale changed')
 check(error < 0.002, 'Strike differs from original motion after transition: ' + str(error))

func verify() -> void:
 preview = load('res://scenes/debug/weapon_preview.tscn').instantiate()
 root.add_child(preview)
 model = preview.get_node('Camera3D/Weapons/Knife')
 model.set_process(false)
 player = model.find_children('*', 'AnimationPlayer', true, false)[0]
 skeleton = model.find_children('*', 'Skeleton3D', true, false)[0]
 reference = load('res://assets/weapons/karambit/karambit.glb').instantiate()
 root.add_child(reference)
 reference_player = reference.find_children('*', 'AnimationPlayer', true, false)[0]
 reference_skeleton = reference.find_children('*', 'Skeleton3D', true, false)[0]
 reference_player.callback_mode_process = AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
 check(model.find_children('*', 'Control', true, false).is_empty(), 'Unexpected UI')
 step(0.2)
 press(KEY_3)
 press(KEY_NONE, MOUSE_BUTTON_LEFT)
 check(player.current_animation == 'draw' and player.current_animation_position >= 0.19, 'Draw was restarted/cancelled during its active phase')
 step(1.2)
 check(player.current_animation == 'idle' and player.current_animation_position < 5.0, 'Draw did not finish in idle')
 press(KEY_F)
 step(0.1)
 var inspect_time := player.current_animation_position
 press(KEY_F)
 check(is_equal_approx(player.current_animation_position, inspect_time), 'F restarted inspect')
 step(1.0)
 var running_inspect_time := player.current_animation_position
 press(KEY_F)
 check(is_equal_approx(player.current_animation_position, running_inspect_time), 'F restarted an ongoing inspect after its repeat timer expired')
 press(KEY_NONE, MOUSE_BUTTON_LEFT)
 check(player.current_animation == 'midslash1', 'Attack did not immediately cancel inspect')
 step(0.07)
 matches_original('midslash1', player.current_animation_position)
 step(0.13)
 matches_original('midslash1', 0.2)
 press(KEY_NONE, MOUSE_BUTTON_RIGHT)
 press(KEY_F)
 press(KEY_3)
 check(player.current_animation == 'midslash1' and player.current_animation_position >= 0.19, 'Active strike was interrupted')
 step(0.33)
 press(KEY_NONE, MOUSE_BUTTON_RIGHT)
 check(player.current_animation == 'midslash1', 'Late input skipped recovery gate')
 step(0.18)
 check(player.current_animation == 'stab' and player.current_animation_position < 0.03, 'Late input was not buffered at recovery boundary')
 step(0.1)
 matches_original('stab', player.current_animation_position)
 step(1.0)
 check(player.current_animation == 'idle' and player.current_animation_position < 5.0, 'Stab did not return to idle')
 press(KEY_NONE, MOUSE_BUTTON_LEFT)
 check(player.current_animation == 'midslash2', 'Second slash did not alternate')
 step(1.1)
 press(KEY_NONE, MOUSE_BUTTON_RIGHT)
 check(player.current_animation == 'stab_miss', 'Second stab variant missing')
 step(1.1)
 press(KEY_NONE, MOUSE_BUTTON_RIGHT)
 check(player.current_animation == 'stab_miss2', 'Third stab variant missing')
 step(1.1)
 press(KEY_F)
 step(4.7)
 check(player.current_animation_position < 5.0, 'Inspect did not finish')
 step(12.6)
 check(player.current_animation == 'idle' and player.current_animation_position < 5.0, 'Idle loop failed')
 # Check several complete idle cycles: playhead must wrap and hands must keep moving.
 var wraps := 0
 var changes := 0
 var previous_time := player.current_animation_position
 var previous_pose := poses(skeleton)
 for sample in range(80):
  step(0.25)
  var current_time := player.current_animation_position
  if current_time < previous_time:
   wraps += 1
  var current_pose := poses(skeleton)
  var movement := 0.0
  for bone in skeleton.get_bone_count():
   movement = maxf(movement, current_pose[bone].origin.distance_to(previous_pose[bone].origin))
  if movement > 0.0001:
   changes += 1
  check(player.is_playing() and current_time < 5.0, 'Idle stopped or entered the frozen tail/inspect')
  previous_time = current_time
  previous_pose = current_pose
 check(wraps >= 4, 'Idle did not wrap across four complete cycles')
 check(changes >= 70, 'Idle contains a prolonged frozen pose')
 # Matching the original strike must hold at common render rates as well.
 for fps in [30, 60, 144]:
  press(KEY_NONE, MOUSE_BUTTON_LEFT)
  for frame in int(fps / 5):
   model.call('_Process', 1.0 / fps)
  matches_original(player.current_animation, player.current_animation_position)
  step(1.1)
 # Inspect never locks either attack, including its first and final frames.
 for offset in [0.01, 0.2, 1.3, 3.9, 4.5]:
  press(KEY_F)
  step(offset)
  press(KEY_NONE, MOUSE_BUTTON_LEFT)
  check(player.current_animation.begins_with('midslash'), 'Inspect blocked attack at ' + str(offset))
  step(1.1)
 # A frozen visual must not freeze the weapon's independent readiness clock.
 press(KEY_F)
 player.speed_scale = 0.0
 step(0.5)
 press(KEY_NONE, MOUSE_BUTTON_LEFT)
 check(player.current_animation.begins_with('midslash'), 'Frozen inspect blocked primary fire')
 step(0.71)
 press(KEY_NONE, MOUSE_BUTTON_RIGHT)
 check(player.current_animation == 'stab', 'Frozen attack animation blocked ready secondary fire')
 player.speed_scale = 1.0
 step(1.1)
 # Conversely, ending the visual early must not unlock a shot early.
 player.speed_scale = 20.0
 press(KEY_NONE, MOUSE_BUTTON_LEFT)
 step(0.1)
 var finished_visual := player.assigned_animation
 press(KEY_NONE, MOUSE_BUTTON_RIGHT)
 check(player.assigned_animation == finished_visual, 'Early visual end cleared weapon cooldown')
 player.speed_scale = 1.0
 step(1.1)
 # Cooldown can be tuned below clip length without editing any animation.
 model.set('PrimaryAttackCooldownSeconds', 0.35)
 press(KEY_NONE, MOUSE_BUTTON_LEFT)
 step(0.36)
 press(KEY_NONE, MOUSE_BUTTON_RIGHT)
 check(player.current_animation == 'stab_miss', 'Attack waited for clip length instead of configured timer')
 step(1.1)
 if failures.is_empty():
  print('KNIFE_FEEL_OK: ', checks, ' checks; inspect interrupt, independent cooldowns with frozen/accelerated visuals, input buffer, authored motion, idle loop, 30/60/144 FPS')
 else:
  print('KNIFE_FEEL_FAILED: ', failures)
 preview.free()
 reference.free()
 OS.delay_msec(100) # Drain stopped playbacks on the audio thread before exit.
 quit(0 if failures.is_empty() else 1)

