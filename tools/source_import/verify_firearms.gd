extends SceneTree

var scene: Node
var weapons: Node
var camera: Camera3D
var checks := 0
var failures: Array[String] = []

func _initialize() -> void:
	call_deferred("verify")

func check(condition: bool, message: String) -> void:
	checks += 1
	if not condition:
		failures.append(message)
		push_error(message)

func key(code: Key, echo := false) -> void:
	var event := InputEventKey.new()
	event.physical_keycode = code
	event.pressed = true
	event.echo = echo
	root.push_input(event)

func mouse(button: MouseButton, pressed := true) -> void:
	var event := InputEventMouseButton.new()
	event.button_index = button
	event.pressed = pressed
	root.push_input(event)
	# Existing checks use right clicks as taps; held secondary has its own checks.
	if button == MOUSE_BUTTON_RIGHT and pressed:
		event = InputEventMouseButton.new()
		event.button_index = button
		event.pressed = false
		root.push_input(event)

func step(seconds: float) -> void:
	while seconds > 0.000001:
		var dt := minf(seconds, 1.0 / 144.0)
		for weapon in weapons.get_children(): weapon.call("_Process", dt)
		seconds -= dt

func player(weapon: Node) -> AnimationPlayer:
	return weapon.find_child("AnimationPlayer", true, false)

func selected(name: String) -> Node:
	for weapon in weapons.get_children():
		check(weapon.visible == (weapon.name == name), "Visibility: " + str(weapon.name) + " selected " + name)
		check(weapon.is_processing_unhandled_input() == (weapon.name == name), "Input: " + str(weapon.name) + " selected " + name)
	return weapons.get_node(NodePath(name))

func verify() -> void:
	scene = load("res://scenes/debug/weapon_preview.tscn").instantiate()
	root.add_child(scene)
	weapons = scene.get_node("Camera3D/Weapons")
	camera = scene.get_node("Camera3D")
	for weapon in weapons.get_children():
		weapon.set_process(false)
		check(weapon.has_node("WeaponSway/Model"), "Uniform hierarchy: " + str(weapon.name))
	selected("Knife")
	step(1.3)
	# Cycle the primary slot; physical numpad keys use the same group.
	for name in ["AK47", "M4A1", "AWP", "Scout", "AUG", "Famas", "G3SG1", "Galil", "M249", "M3",
		"MAC10", "MP5Navy", "P90", "SG550", "SG552", "TMP", "UMP45", "XM1014", "AK47"]:
		key(KEY_1)
		selected(name)
		step(1.1)
	key(KEY_1, true)
	selected("AK47")
	key(KEY_KP_1)
	var m4 := selected("M4A1")
	# Deploy, suppressor operation, and reload must block conflicting actions.
	mouse(MOUSE_BUTTON_RIGHT)
	check(player(m4).current_animation == "draw_unsil", "M4 secondary blocked during draw")
	step(1.1)
	mouse(MOUSE_BUTTON_RIGHT)
	check(player(m4).current_animation == "add_silencer", "M4 attaches silencer")
	var shots: int = m4.get("ShotsFired")
	mouse(MOUSE_BUTTON_LEFT)
	key(KEY_R)
	mouse(MOUSE_BUTTON_RIGHT)
	check(player(m4).current_animation == "add_silencer" and m4.get("ShotsFired") == shots, "Silencer change owns the action")
	mouse(MOUSE_BUTTON_LEFT, false)
	step(2.1)
	check(m4.get("Silenced") and player(m4).current_animation == "idle", "M4 silencer commits on completion")
	mouse(MOUSE_BUTTON_LEFT)
	check(player(m4).current_animation == "shoot1", "M4 uses silenced fire")
	step(0.31)
	check(m4.get("ShotsFired") >= shots + 4, "M4 holds automatic fire")
	mouse(MOUSE_BUTTON_LEFT, false)
	key(KEY_R)
	check(player(m4).current_animation == "reload", "M4 silenced reload")
	mouse(MOUSE_BUTTON_RIGHT)
	check(player(m4).current_animation == "reload", "Secondary blocked during reload")
	step(3.2)
	mouse(MOUSE_BUTTON_RIGHT)
	step(0.5)
	key(KEY_3)
	selected("Knife")
	key(KEY_Q)
	selected("M4A1")
	check(m4.get("Silenced") and player(m4).current_animation == "draw", "Holster cancels pending detach and preserves committed silencer")
	step(1.1)
	mouse(MOUSE_BUTTON_RIGHT)
	step(2.1)
	check(not m4.get("Silenced") and player(m4).current_animation == "idle_unsil", "M4 removes silencer")
	# Pistols fire once per press, then permit another shot after release/cooldown.
	key(KEY_2)
	var deagle := selected("Deagle")
	step(1.1)
	mouse(MOUSE_BUTTON_LEFT)
	step(1.0)
	check(deagle.get("ShotsFired") == 1, "Deagle is semiautomatic")
	mouse(MOUSE_BUTTON_LEFT, false)
	mouse(MOUSE_BUTTON_LEFT)
	check(deagle.get("ShotsFired") == 2, "Deagle fires on a new press")
	mouse(MOUSE_BUTTON_LEFT, false)
	step(0.3)
	key(KEY_R)
	check(player(deagle).current_animation == "reload", "Deagle reload")
	key(KEY_KP_2)
	var usp := selected("USP")
	step(1.1)
	mouse(MOUSE_BUTTON_RIGHT)
	check(player(usp).current_animation == "add_silencer", "USP attaches silencer")
	step(3.2)
	check(usp.get("Silenced") and player(usp).current_animation == "idle", "USP silencer commits")
	mouse(MOUSE_BUTTON_LEFT)
	step(0.5)
	check(usp.get("ShotsFired") == 1, "USP is semiautomatic")
	mouse(MOUSE_BUTTON_LEFT, false)
	mouse(MOUSE_BUTTON_RIGHT)
	step(3.2)
	check(not usp.get("Silenced") and player(usp).current_animation == "idle_unsil", "USP removes silencer")
	key(KEY_R)
	check(player(usp).current_animation == "reload_unsil", "USP unsilenced reload")
	key(KEY_Q)
	selected("Deagle")
	check(player(deagle).current_animation == "draw", "Previous pistol draws instead of resuming reload")
	key(KEY_Q)
	selected("USP")
	step(4.0)
	check(player(usp).current_animation == "idle_unsil", "Holster clears pistol actions and held input")
	# Source's horizontal 40-degree FOV becomes 30.5369 degrees vertically in Godot.
	key(KEY_1)
	selected("M4A1")
	key(KEY_1)
	for name in ["AWP", "Scout"]:
		var sniper := selected(name)
		mouse(MOUSE_BUTTON_RIGHT)
		check(sniper.get("ZoomLevel") == 0, name + " zoom blocked during deploy")
		step(1.1)
		mouse(MOUSE_BUTTON_RIGHT)
		check(sniper.get("ZoomLevel") == 1 and is_equal_approx(camera.fov, 30.536943), name + " first zoom")
		check(not sniper.get_node("WeaponSway/Model").visible, name + " scoped viewmodel hidden")
		mouse(MOUSE_BUTTON_RIGHT)
		check(sniper.get("ZoomLevel") == 1, name + " zoom debounce")
		step(0.3)
		mouse(MOUSE_BUTTON_RIGHT)
		check(sniper.get("ZoomLevel") == 2 and camera.fov < 20, name + " second zoom")
		step(0.3)
		mouse(MOUSE_BUTTON_RIGHT)
		check(sniper.get("ZoomLevel") == 0 and is_equal_approx(camera.fov, 58.0), name + " zoom cycles off")
		step(0.3)
		mouse(MOUSE_BUTTON_RIGHT)
		mouse(MOUSE_BUTTON_LEFT)
		check(sniper.get("ZoomLevel") == 0 and is_equal_approx(camera.fov, 58.0), name + " shot leaves scope for bolt cycle")
		step(0.6)
		check(sniper.get("ShotsFired") == 1, name + " bolt cycle prevents rapid fire")
		check(sniper.get("ZoomLevel") == 0, name + " scope stays released during the bolt cycle")
		mouse(MOUSE_BUTTON_LEFT, false)
		step(1.1)
		check(sniper.get("ZoomLevel") == 1 and is_equal_approx(camera.fov, 30.536943), name + " scope returns after the bolt cycle")
		mouse(MOUSE_BUTTON_RIGHT)
		key(KEY_R)
		check(sniper.get("ZoomLevel") == 0 and is_equal_approx(camera.fov, 58.0), name + " reload resets zoom")
		step(4.0)
		mouse(MOUSE_BUTTON_RIGHT)
		key(KEY_3)
		selected("Knife")
		check(is_equal_approx(camera.fov, 58.0), name + " holster restores camera")
		key(KEY_Q)
		selected(name)
		check(sniper.get("ZoomLevel") == 0 and sniper.get_node("WeaponSway/Model").visible, name + " returns unscoped")
		key(KEY_1)
	# AUG follows Scout; traverse the added primary weapons to return to AK.
	for index in 14: key(KEY_1)
	selected("AK47")
	for weapon in weapons.get_children():
		if weapon.name != "Knife":
			for mesh in weapon.find_children("*", "MeshInstance3D", true, false):
				for surface in mesh.mesh.get_surface_count():
					var material: Material = mesh.get_active_material(surface)
					if material is ShaderMaterial:
						check(weapon.name == "AWP" and material.shader.resource_path.ends_with("scope_glass.gdshader"), "AWP lens uses opaque procedural glass")
					else:
						check(material.cull_mode == BaseMaterial3D.CULL_BACK, "Outward shading: " + str(weapon.name))
	# Unanimated M4 root must receive SourceIO's coordinate conversion too.
	# Without it the hand bones/mesh sit behind the camera despite valid skinning.
	var m4_player := player(m4)
	m4_player.play("idle_unsil")
	m4_player.seek(0.2, true)
	var skeleton: Skeleton3D = m4.find_child("Skeleton3D", true, false)
	skeleton.force_update_all_bone_transforms()
	var left_hand := skeleton.find_bone("v_weapon.Left_Hand")
	var hand_position := skeleton.global_transform * skeleton.get_bone_global_pose(left_hand).origin
	check(hand_position.z < -0.1 and hand_position.z > -0.8, "M4 support hand is in front of camera")
	check(scene.find_children("*", "Control", true, false).is_empty(), "No scope overlay or HUD")
	print("FIREARMS ", checks, " checks; failures: ", failures)
	scene.free()
	OS.delay_msec(100) # Drain stopped playbacks on the audio thread before exit.
	quit(0 if failures.is_empty() else 1)
