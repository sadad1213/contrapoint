extends SceneTree

func _initialize() -> void:
	call_deferred("run")

func run() -> void:
	var world := Node3D.new()
	root.add_child(world)
	current_scene = world
	var environment := WorldEnvironment.new()
	environment.environment = Environment.new()
	environment.environment.background_mode = Environment.BG_COLOR
	environment.environment.background_color = Color(0.18, 0.2, 0.23)
	environment.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.environment.ambient_light_color = Color.WHITE
	environment.environment.ambient_light_energy = 0.6
	world.add_child(environment)
	var light := DirectionalLight3D.new()
	light.rotation_degrees = Vector3(-55, -30, 0)
	light.light_energy = 1.8
	light.shadow_enabled = true
	world.add_child(light)
	var floor_body := StaticBody3D.new()
	var floor_shape := CollisionShape3D.new()
	var shape := BoxShape3D.new()
	shape.size = Vector3(8, 0.2, 6)
	floor_shape.shape = shape
	floor_body.add_child(floor_shape)
	floor_body.position.y = -0.1
	var floor_mesh := MeshInstance3D.new()
	var box := BoxMesh.new()
	box.size = shape.size
	floor_mesh.mesh = box
	var material := StandardMaterial3D.new()
	material.albedo_color = Color(0.46, 0.48, 0.5)
	material.roughness = 1.0
	floor_mesh.material_override = material
	floor_body.add_child(floor_mesh)
	world.add_child(floor_body)
	var camera := Camera3D.new()
	world.add_child(camera)
	camera.position = Vector3(0, 3.6, 3.6)
	camera.look_at(Vector3(0, 0, 0))
	camera.fov = 42
	camera.current = true
	var names := ["ak47", "m4a1", "awp", "scout", "deagle", "usp"]
	for i in names.size():
		var pickup = load("res://weapons/weapon_pickup.tscn").instantiate()
		pickup.set("WeaponClass", "weapon_" + names[i])
		world.add_child(pickup)
		pickup.position = Vector3((i % 3 - 1) * 1.15, 0.5, (i / 3 - 0.5) * 1.3)
		pickup.angular_velocity = Vector3(1, 1, -1) * 0.3
	for i in 180:
		await physics_frame
	await RenderingServer.frame_post_draw
	var output := OS.get_environment("TEMP").path_join("contrapoint_weapon_pickups.png")
	root.get_texture().get_image().save_png(output)
	print("WEAPON_PICKUP_RENDER ", output)
	quit()
