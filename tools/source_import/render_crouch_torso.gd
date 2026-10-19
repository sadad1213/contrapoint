extends SceneTree

# Capture crouched weapon poses from directly behind each agent.
func _initialize() -> void:
	call_deferred("render_poses")

func render_poses() -> void:
	root.size = Vector2i(1100, 750)
	AudioServer.set_bus_mute(0, true)
	var stage := Node3D.new()
	root.add_child(stage)
	var environment := WorldEnvironment.new()
	environment.environment = Environment.new()
	environment.environment.background_mode = Environment.BG_COLOR
	environment.environment.background_color = Color("343b46")
	environment.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.environment.ambient_light_energy = 0.8
	stage.add_child(environment)
	var light := DirectionalLight3D.new()
	light.rotation_degrees = Vector3(-40, -30, 0)
	stage.add_child(light)
	var floor_body := StaticBody3D.new()
	var collision := CollisionShape3D.new()
	collision.shape = BoxShape3D.new()
	collision.shape.size = Vector3(100, 1, 100)
	floor_body.position.y = -0.5
	floor_body.add_child(collision)
	stage.add_child(floor_body)
	var player = load("res://scenes/player/player.tscn").instantiate()
	player.set("DebugControls", false)
	stage.add_child(player)
	player.call("SetMouseCaptured", true)
	player.get_node("WeaponLayer").hide()
	player.get_node("HUD").hide()
	var camera := Camera3D.new()
	camera.cull_mask = 5
	stage.add_child(camera)
	camera.position = Vector3(0, 1.6, 3.7)
	camera.look_at(Vector3(0, 0.8, 0))
	camera.fov = 38
	camera.make_current()
	for frame in 30:
		await physics_frame
	Input.action_press("duck")
	for frame in 60:
		await physics_frame
	var inventory = player.get_node("View/Camera3D/Weapons")
	for agent in ["phoenix", "arctic", "guerilla", "leet"]:
		assert(player.call("SetAgent", agent))
		for slot in [2, 1, 0]:
			inventory.call("EquipSlot", slot)
			for frame in 60:
				await physics_frame
			await RenderingServer.frame_post_draw
			assert(root.get_texture().get_image().save_png("res://.godot/crouch_torso_%s_%d.png" % [agent, slot]) == OK)
			print("CROUCH_POSE ", agent, " ", player.get_node("Body").get("UpperClip"))
	Input.action_release("duck")
	stage.free()
	quit()
