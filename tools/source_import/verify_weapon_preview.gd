extends SceneTree

var scene: Node
var knife: Node3D
var rifle: Node3D
var knife_player: AnimationPlayer
var rifle_player: AnimationPlayer
var failures: Array[String] = []
var checks := 0

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

func trigger(pressed: bool) -> void:
	var event := InputEventMouseButton.new()
	event.button_index = MOUSE_BUTTON_LEFT
	event.pressed = pressed
	root.push_input(event)

func step(seconds: float) -> void:
	while seconds > 0.000001:
		var dt := minf(seconds, 1.0 / 144.0)
		knife.call("_Process", dt)
		rifle.call("_Process", dt)
		seconds -= dt

func selected(want_rifle: bool) -> void:
	check(rifle.visible == want_rifle and knife.visible != want_rifle, "Exactly one weapon must be visible")
	check(rifle.is_processing_unhandled_input() == want_rifle, "Rifle input activation")
	check(knife.is_processing_unhandled_input() != want_rifle, "Knife input activation")

func verify() -> void:
	scene = load("res://scenes/debug/weapon_preview.tscn").instantiate()
	root.add_child(scene)
	knife = scene.get_node("Camera3D/Weapons/Knife")
	rifle = scene.get_node("Camera3D/Weapons/AK47")
	for weapon in [knife, rifle]:
		weapon.set_process(false)
		check(weapon.has_node("WeaponSway/Model"), "Shared weapon hierarchy")
	knife_player = knife.find_child("AnimationPlayer", true, false)
	rifle_player = rifle.find_child("AnimationPlayer", true, false)
	selected(false)
	step(1.3)
	key(KEY_F)
	step(0.2)
	key(KEY_1)
	selected(true)
	check(rifle_player.current_animation == "ak47_draw", "Rifle deploy on select")
	var hidden_knife_clip := knife_player.current_animation
	trigger(true)
	check(rifle_player.current_animation == "ak47_draw", "Fire blocked during deploy")
	step(0.3)
	key(KEY_1, true)
	check(rifle_player.current_animation_position > 0.29, "Key echo must not cycle the primary slot")
	step(0.8)
	check(rifle_player.current_animation.begins_with("ak47_fire"), "Held trigger fires after deploy")
	check(knife_player.current_animation == hidden_knife_clip, "Hidden knife ignores fire")
	trigger(false)
	step(0.8)
	key(KEY_R)
	check(rifle_player.current_animation == "ak47_reload", "R plays reload")
	step(0.4)
	key(KEY_Q)
	selected(false)
	check(knife_player.current_animation == "draw", "Q deploys previous weapon")
	key(KEY_Q, true)
	selected(false)
	trigger(true)
	check(knife_player.current_animation == "draw", "Knife cannot attack immediately after switching")
	trigger(false)
	step(1.3)
	key(KEY_Q)
	selected(true)
	check(rifle_player.current_animation == "ak47_draw", "Returning cancels old reload")
	step(1.8)
	check(rifle_player.current_animation == "ak47_idle", "Holster clears held trigger")
	key(KEY_KP_3)
	selected(false)
	step(1.3)
	trigger(true)
	check(knife_player.current_animation.begins_with("midslash"), "Knife attacks still work")
	trigger(false)
	for index in 12:
		key(KEY_Q)
		selected(index % 2 == 0)
		step(0.02)
	step(1.3)
	check(scene.find_children("*", "Control", true, false).is_empty(), "Preview has no HUD")
	print("WEAPON_PREVIEW ", checks, " checks; failures: ", failures)
	scene.free()
	quit(0 if failures.is_empty() else 1)
