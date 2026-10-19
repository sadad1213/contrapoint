extends SceneTree

func _initialize() -> void:
	call_deferred("render_player")

func render_player() -> void:
	root.size = Vector2i(1280, 720)
	var scene := load("res://scenes/maps/dust2.tscn").instantiate() as Node3D
	root.add_child(scene)
	for frame in 90:
		await physics_frame
	var player := scene.get_node("Player") as CharacterBody3D
	assert(player.is_on_floor())
	# Look along the T spawn exit, then check a rifle in the outdoor lighting.
	player.rotation.y = deg_to_rad(-150)
	await process_frame
	await RenderingServer.frame_post_draw
	assert(root.get_texture().get_image().save_png("res://assets/maps/de_dust2/source/player_knife.png") == OK)
	var key := InputEventKey.new()
	key.physical_keycode = KEY_1
	key.pressed = true
	root.push_input(key)
	for frame in 90:
		await physics_frame
	await RenderingServer.frame_post_draw
	assert(root.get_texture().get_image().save_png("res://assets/maps/de_dust2/source/player_ak47.png") == OK)
	print("MAP_PLAYER_RENDER_OK")
	scene.queue_free()
	quit()
