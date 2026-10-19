extends SceneTree

func _initialize() -> void:
	call_deferred("render_damage")

func render_damage() -> void:
	seed(12345)
	root.size = Vector2i(1280, 720)
	var world := Node3D.new()
	root.add_child(world)
	var environment := WorldEnvironment.new()
	environment.environment = Environment.new()
	environment.environment.background_mode = Environment.BG_COLOR
	environment.environment.background_color = Color(0.18, 0.2, 0.24)
	environment.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.environment.ambient_light_color = Color.WHITE
	environment.environment.ambient_light_energy = 0.8
	world.add_child(environment)
	var light := DirectionalLight3D.new()
	light.rotation_degrees = Vector3(-40, -30, 0)
	world.add_child(light)
	var ground := StaticBody3D.new()
	var collision := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(20, 1, 20)
	collision.shape = box
	ground.add_child(collision)
	ground.position.y = -0.5
	world.add_child(ground)
	var target = load("res://scenes/player/player.tscn").instantiate()
	target.LocalInput = false
	world.add_child(target)
	var attacker = load("res://scenes/player/player.tscn").instantiate()
	attacker.LocalInput = false
	attacker.Team = 2
	attacker.position = Vector3(0, 0, 3)
	world.add_child(attacker)
	var camera := Camera3D.new()
	world.add_child(camera)
	camera.position = Vector3(1.6, 1.6, 3)
	camera.look_at(Vector3(0, 1.2, 0))
	camera.current = true
	await physics_frame
	target.velocity = Vector3.DOWN * 0.1
	target.move_and_slide()
	await process_frame
	target.call("TakeTraceDamage", 36.0, attacker, 2, Vector3(0, 1.3, 0.18), Vector3.FORWARD, 1.55, false, true)
	for child in world.get_children():
		if child.name == "DamageImpact":
			child.set_process(false)
			child.call("_Process", 0.08)
	attacker.visible = false
	await process_frame
	await process_frame
	await RenderingServer.frame_post_draw
	var output := "res://.godot/damage_preview.png"
	assert(root.get_texture().get_image().save_png(output) == OK)
	print("DAMAGE_PREVIEW ", output)
	world.free()
	quit()
