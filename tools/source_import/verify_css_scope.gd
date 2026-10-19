extends SceneTree

var player: Node
var inventory: Node
var scope: Control

func _initialize() -> void:
	call_deferred("verify")

func settle() -> void:
	for frame in 4:
		await process_frame

func capture(label: String) -> void:
	await settle()
	await RenderingServer.frame_post_draw
	assert(root.get_texture().get_image().save_png("res://.godot/scope_" + label + ".png") == OK)

func check_mouse(expected_scale: float) -> void:
	assert(is_equal_approx(player.get("MouseZoomScale"), expected_scale))
	var yaw_before: float = player.rotation.y
	var pitch_before: float = player.get("AimPitch")
	var motion = InputEventMouseMotion.new()
	motion.relative = Vector2(12, 8)
	player.call("_Input", motion)
	var sensitivity: float = player.get("MouseSensitivity")
	assert(abs(player.rotation.y - yaw_before + 12 * sensitivity * expected_scale) < 0.00001)
	assert(abs(player.get("AimPitch") - pitch_before + 8 * sensitivity * expected_scale) < 0.00001)
	motion.relative = -motion.relative
	player.call("_Input", motion)

func tap_scope(gun: Node) -> void:
	gun.call("HandleCommand", "attack2", true)
	gun.call("HandleCommand", "attack2", false)

func check_held_scope(gun: Node, console: Node) -> void:
	console.call("Execute", "+attack2")
	assert(gun.get("ZoomLevel") == 1)
	gun.call("_Process", 0.299)
	assert(gun.get("ZoomLevel") == 1)
	gun.call("_Process", 0.001)
	assert(gun.get("ZoomLevel") == 2)
	gun.call("_Process", 0.3)
	assert(gun.get("ZoomLevel") == 0)
	gun.call("_Process", 0.3)
	assert(gun.get("ZoomLevel") == 1)
	console.call("Execute", "-attack2")
	gun.call("_Process", 1.0)
	assert(gun.get("ZoomLevel") == 1) # Release keeps the selected magnification.
	console.call("Execute", "+attack2")
	assert(gun.get("ZoomLevel") == 2)
	gun.call("SetInputEnabled", false)
	gun.call("_Process", 0.5)
	assert(gun.get("ZoomLevel") == 2) # Menu/console input cancellation stops repeats.
	console.call("Execute", "-attack2")
	gun.call("SetInputEnabled", true)
	console.call("Execute", "+attack2")
	assert(gun.get("ZoomLevel") == 0)
	gun.call("_Notification", Node.NOTIFICATION_APPLICATION_FOCUS_OUT)
	gun.call("_Process", 0.5)
	assert(gun.get("ZoomLevel") == 0)
	console.call("Execute", "-attack2")
	# A held button waits for deploy, then starts zooming without a second press.
	gun.call("Equip")
	console.call("Execute", "+attack2")
	assert(gun.get("ZoomLevel") == 0)
	gun.call("_Process", 2.0)
	assert(gun.get("ZoomLevel") == 1)
	console.call("Execute", "-attack2")
	gun.call("Holster")
	gun.call("Equip")
	gun.call("_Process", 2.0)
	assert(gun.get("ZoomLevel") == 0)
	print("CSS_SCOPE_HOLD_OK ", gun.name)

func verify() -> void:
	root.mode = Window.MODE_WINDOWED
	root.size = Vector2i(1280, 720)
	var scene = load("res://scenes/maps/dust2.tscn").instantiate()
	root.add_child(scene)
	player = scene.get_node("Player")
	player.set_physics_process(false)
	await physics_frame
	await physics_frame
	player.rotation.y = deg_to_rad(-150)
	player.get("Menu").call("SetOpen", false)
	player.call("JoinTeam", 2)
	player.call("JoinClass", 1)
	player.get("TeamMenu").call("Close")
	player.call("SetMouseCaptured", true)
	var console = player.get("Console")
	console.call("Execute", "sensitivity 3; m_yaw 0.022; m_pitch 0.022; m_filter 0; m_rawinput 0; cl_mouseenable 1; zoom_sensitivity_ratio 1")
	inventory = player.get_node("View/Camera3D/Weapons")
	scope = player.get_node("HUD/Scope")
	var crosshair = player.get_node("HUD/Crosshair")
	for weapon_name in ["awp", "scout"]:
		var gun = inventory.call("AddWeapon", load("res://weapons/" + weapon_name + "_view.tscn"), true, true)
		gun.call("_Process", 2.0)
		gun.set_process(false)
		check_held_scope(gun, console)
		await settle()
		assert(not scope.visible and not crosshair.visible)
		check_mouse(1)
		for level in [1, 2]:
			tap_scope(gun)
			gun.call("_Process", 0.3)
			await settle()
			assert(gun.get("ZoomLevel") == level and scope.visible and not crosshair.visible)
			assert(not gun.get_node("WeaponSway/Model").visible)
			var source_fov: float = 40.0 if level == 1 else (10.0 if weapon_name == "awp" else 15.0)
			var camera = player.get_node("View/Camera3D")
			assert(is_equal_approx(camera.fov, rad_to_deg(2 * atan(tan(deg_to_rad(source_fov) / 2) * 3 / 4))))
			for ratio in [0.0, 0.5, 1.0, 2.0]:
				console.call("Execute", "zoom_sensitivity_ratio " + str(ratio))
				assert(is_equal_approx(player.get("ZoomSensitivityRatio"), ratio))
				check_mouse(source_fov / 90.0 * ratio)
			console.call("Execute", "zoom_sensitivity_ratio 1")
			await capture(weapon_name + "_zoom" + str(level) + "_720")
		if weapon_name == "awp":
			for resolution in [Vector2i(1920, 1080), Vector2i(1024, 768), Vector2i(1281, 721)]:
				root.size = resolution
				await capture(weapon_name + "_" + str(resolution.x) + "x" + str(resolution.y))
			root.size = Vector2i(1280, 720)
			crosshair.set("Enabled", false)
			await settle()
			assert(scope.visible)
			crosshair.set("Enabled", true)
			player.get_node("HUD/Status").set("Enabled", false)
			await settle()
			assert(not scope.visible)
			player.get_node("HUD/Status").set("Enabled", true)
			player.call("SetThirdPerson", true)
			await settle()
			assert(not scope.visible)
			player.call("SetThirdPerson", false)
		tap_scope(gun)
		await settle()
		assert(gun.get("ZoomLevel") == 0 and not scope.visible and gun.get_node("WeaponSway/Model").visible)
		console.call("Execute", "zoom_sensitivity_ratio 2")
		check_mouse(1)
		console.call("Execute", "zoom_sensitivity_ratio 1")
		gun.call("_Process", 0.3)
		tap_scope(gun)
		gun.call("HandleCommand", "attack", true)
		gun.call("HandleCommand", "attack", false)
		await settle()
		assert(gun.get("ZoomLevel") == 0 and not scope.visible)
		gun.call("_Process", 2.0)
		tap_scope(gun)
		gun.call("HandleCommand", "reload", true)
		await settle()
		assert(gun.get("ZoomLevel") == 0 and not scope.visible)
		gun.call("_Process", 5.0)
		tap_scope(gun)
		await settle()
		assert(scope.visible)
		inventory.call("EquipSlot", 2)
		await settle()
		assert(not scope.visible and crosshair.visible and gun.get("ZoomLevel") == 0)
		check_mouse(1)
		print("CSS_SCOPE_WEAPON_OK ", weapon_name)
	console.call("Execute", "zoom_sensitivity_ratio 0.75; host_writeconfig scope_checks; zoom_sensitivity_ratio 1; exec scope_checks")
	assert(is_equal_approx(player.get("ZoomSensitivityRatio"), 0.75))
	console.call("Execute", "zoom_sensitivity_ratio 1")
	var gun = inventory.call("AddWeapon", load("res://weapons/awp_view.tscn"), true, true)
	gun.call("_Process", 2.0)
	gun.set_process(false)
	tap_scope(gun)
	await settle()
	assert(scope.visible)
	player.call("TakeDamage", 1000)
	await settle()
	assert(not scope.visible and not crosshair.visible)
	scene.free()
	print("CSS_SCOPE_CHECKS_PASSED")
	quit()
