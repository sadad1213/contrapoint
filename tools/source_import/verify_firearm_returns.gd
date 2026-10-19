extends SceneTree

var checks := 0
var failures: Array[String] = []

func _initialize() -> void:
	call_deferred("verify")

func check(condition: bool, message: String) -> void:
	checks += 1
	if not condition:
		failures.append(message)
		push_error(message)

func poses(skeleton: Skeleton3D) -> Array[Transform3D]:
	var result: Array[Transform3D] = []
	for bone in skeleton.get_bone_count():
		result.append(skeleton.get_bone_pose(bone))
	return result

func pose_error(a: Array[Transform3D], b: Array[Transform3D]) -> Vector2:
	var error := Vector2.ZERO
	for bone in a.size():
		error.x = maxf(error.x, a[bone].origin.distance_to(b[bone].origin))
		error.y = maxf(error.y, a[bone].basis.get_rotation_quaternion().normalized().angle_to(
			b[bone].basis.get_rotation_quaternion().normalized()))
	return error

func step(weapon: Node, seconds: float, fps: int) -> void:
	while seconds > 0.0000001:
		var dt := minf(seconds, 1.0 / fps)
		weapon.call("_Process", dt)
		seconds -= dt

func mouse(weapon: Node, button: MouseButton, pressed: bool) -> void:
	var event := InputEventMouseButton.new()
	event.button_index = button
	event.pressed = pressed
	weapon.call("_UnhandledInput", event)

func verify_return(weapon: Node, player: AnimationPlayer, skeleton: Skeleton3D, fps: int) -> void:
	var label := "%s silenced=%s %d FPS" % [weapon.name, weapon.get("Silenced"), fps]
	mouse(weapon, MOUSE_BUTTON_LEFT, true)
	mouse(weapon, MOUSE_BUTTON_LEFT, false)
	var shot := player.current_animation
	check(not shot.contains("idle"), label + ": shot started")
	# Approach the clip end, then cross it by a tiny step. A direct idle
	# assignment produces the whole pose discontinuity even at this small dt.
	step(weapon, player.current_animation_length - player.current_animation_position - 0.00001, fps)
	var before := poses(skeleton)
	weapon.call("_Process", 0.00002)
	var error := pose_error(before, poses(skeleton))
	check(player.current_animation.contains("idle"), label + ": returned to idle")
	check(error.x < 0.0001 and error.y < 0.002,
		label + ": return snapped: position=%f angle=%f" % [error.x, error.y])
	step(weapon, weapon.get("ReturnTransitionSeconds") + 0.05, fps)
	var displayed := poses(skeleton)
	# After the blend, the displayed pose must match the authored idle.
	player.seek(player.current_animation_position, true)
	error = pose_error(displayed, poses(skeleton))
	check(error.x < 0.0001 and error.y < 0.002, label + ": blend did not settle to authored idle")
	step(weapon, 2.0, fps)

func verify() -> void:
	var scene: Node = load("res://scenes/debug/weapon_preview.tscn").instantiate()
	root.add_child(scene)
	var weapons: Node = scene.get_node("Camera3D/Weapons")
	for weapon in weapons.get_children():
		weapon.set_process(false)
		weapon.call("Holster")
	for weapon in weapons.get_children():
		if weapon.name == "Knife":
			continue
		var player: AnimationPlayer = weapon.find_child("AnimationPlayer", true, false)
		var skeleton: Skeleton3D = weapon.find_children("*", "Skeleton3D", true, false)[0]
		weapon.call("Equip")
		step(weapon, 2.0, 144)
		for fps in [30, 60, 144]:
			# Cover all randomized/alternating shot clips, including both
			# suppressed and unsuppressed variants where applicable.
			for variant in range(3):
				verify_return(weapon, player, skeleton, fps)
		if weapon.name in ["M4A1", "USP"]:
			mouse(weapon, MOUSE_BUTTON_RIGHT, true)
			step(weapon, 4.0, 144)
			check(weapon.get("Silenced"), str(weapon.name) + ": silencer committed")
			for fps in [30, 60, 144]:
				for variant in range(3):
					verify_return(weapon, player, skeleton, fps)
		weapon.call("Holster")
	print("FIREARM_RETURNS: ", checks, " checks; failures: ", failures)
	scene.free()
	quit(0 if failures.is_empty() else 1)
