extends SceneTree

func _initialize() -> void:
	call_deferred("render_agents")

func render_agents() -> void:
	var agents := OS.get_cmdline_user_args()
	if agents.is_empty():
		agents = PackedStringArray(["phoenix", "arctic", "guerilla", "leet", "urban", "gsg9", "sas", "gign"])
	root.size = Vector2i(1280, 720)
	AudioServer.set_bus_mute(0, true)
	var stage := Node3D.new()
	root.add_child(stage)
	var environment := WorldEnvironment.new()
	environment.environment = Environment.new()
	environment.environment.background_mode = Environment.BG_COLOR
	environment.environment.background_color = Color("343b46")
	environment.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.environment.ambient_light_color = Color("c7d4e8")
	environment.environment.ambient_light_energy = 0.6
	stage.add_child(environment)
	var light := DirectionalLight3D.new()
	light.rotation_degrees = Vector3(-45, -30, 0)
	light.light_energy = 1.5
	stage.add_child(light)
	var floor_body := StaticBody3D.new()
	var collision := CollisionShape3D.new()
	collision.shape = BoxShape3D.new()
	collision.shape.size = Vector3(100, 1, 100)
	floor_body.position.y = -0.5
	floor_body.add_child(collision)
	var mesh := MeshInstance3D.new()
	mesh.mesh = BoxMesh.new()
	mesh.mesh.size = Vector3(100, 1, 100)
	floor_body.add_child(mesh)
	stage.add_child(floor_body)
	var camera := Camera3D.new()
	camera.cull_mask = 5
	stage.add_child(camera)
	camera.position = Vector3(3, 1.9, -3.5)
	camera.look_at(Vector3(0, 0.95, 0))
	camera.fov = 40
	camera.current = true
	var player = load("res://scenes/player/player.tscn").instantiate()
	player.set("LocalInput", true)
	player.set("DebugControls", false)
	stage.add_child(player)
	assert(player.call("SetAgent", agents[0]))
	player.call("SetMouseCaptured", false)
	player.get_node("WeaponLayer").hide()
	player.get_node("HUD").hide()
	camera.make_current()
	var body = player.get_node("Body")
	var inventory = player.get_node("View/Camera3D/Weapons")
	inventory.call("SetEnabled", true, true)
	for i in 20:
		await physics_frame
	for slot in [0, 1, 2, 3]:
		inventory.call("EquipSlot", slot)
		for frame in 30:
			await process_frame
		await RenderingServer.frame_post_draw
		assert(root.get_texture().get_image().save_png("res://.godot/agent_slot_%d.png" % slot) == OK)
	player.call("SetMouseCaptured", true)
	inventory.call("EquipSlot", 0)
	for side in ["move_left", "move_right", "move_left"]:
		Input.action_press(side)
		for frame in 45:
			await physics_frame
			if frame in [3, 25]:
				camera.position = player.position + Vector3(3, 1.9, -3.5)
				camera.look_at(player.position + Vector3(0, 0.95, 0))
				await RenderingServer.frame_post_draw
				assert(root.get_texture().get_image().save_png("res://.godot/strafe_%s_%d.png" % [side, frame]) == OK)
		Input.action_release(side)
	Input.action_press("move_forward")
	for frame in 50:
		await physics_frame
	player.set_physics_process(false)
	camera.position = player.position + Vector3(3, 1.9, -3.5)
	camera.look_at(player.position + Vector3(0, 0.95, 0))
	await process_frame
	await RenderingServer.frame_post_draw
	assert(root.get_texture().get_image().save_png("res://assets/players/%s/source/run.png" % agents[0]) == OK)
	Input.action_release("move_forward")
	Input.action_press("duck")
	player.set_physics_process(true)
	for frame in 60:
		await physics_frame
	player.set_physics_process(false)
	camera.position = player.position + Vector3(3, 1.9, -3.5)
	camera.look_at(player.position + Vector3(0, 0.8, 0))
	await process_frame
	await RenderingServer.frame_post_draw
	assert(root.get_texture().get_image().save_png("res://assets/players/%s/source/crouch.png" % agents[0]) == OK)
	var gun = inventory.get("ActiveWeapon")
	gun.call("_Process", 2.0)
	var reload := InputEventKey.new()
	reload.physical_keycode = KEY_R
	reload.pressed = true
	gun.call("_UnhandledInput", reload)
	gun.call("_Process", 0.9)
	gun.set_process(false)
	for frame in 30:
		await process_frame
	await RenderingServer.frame_post_draw
	assert(root.get_texture().get_image().save_png("res://assets/players/%s/source/reload.png" % agents[0]) == OK)
	Input.action_release("duck")
	for agent in agents:
		assert(player.call("SetAgent", agent))
		for frame in 20:
			await process_frame
		await RenderingServer.frame_post_draw
		assert(root.get_texture().get_image().save_png("res://assets/players/%s/source/preview.png" % agent) == OK)
	print("AGENT_POSE ", body.get("LowerClip"), " ", body.get("UpperClip"))
	var skeleton: Skeleton3D = body.get("Skeleton")
	print("AGENT_SKELETON ", skeleton.get_path())
	for i in skeleton.get_bone_count():
		if "weapon_bone" in skeleton.get_bone_name(i) or "Hand" in skeleton.get_bone_name(i) or "Head" in skeleton.get_bone_name(i):
			print(skeleton.get_bone_name(i), " ", skeleton.global_transform * skeleton.get_bone_global_pose(i))
	print("WORLD_WEAPON ", body.get("WorldWeapon").global_transform)
	stage.free()
	quit()
