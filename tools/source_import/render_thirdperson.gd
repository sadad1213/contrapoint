extends SceneTree

func _initialize() -> void:
	call_deferred("render_player")

func render_player() -> void:
	root.size = Vector2i(1280, 720)
	AudioServer.set_bus_mute(0, true)
	var scene = load("res://scenes/maps/dust2.tscn").instantiate()
	root.add_child(scene)
	var player = scene.get_node("Player")
	for frame in 80:
		await physics_frame
	player.rotation.y = deg_to_rad(-150)
	player.call("SetMouseCaptured", true)
	player.get_node("View/Camera3D/Weapons").call("EquipSlot", 0)
	player.get("Console").call("Execute", "sv_cheats 1; thirdperson")
	for frame in 100:
		await physics_frame
	await RenderingServer.frame_post_draw
	assert(player.get("IsThirdPerson"))
	assert(root.get_texture().get_image().save_png("res://assets/players/phoenix/source/thirdperson_dust2.png") == OK)
	print("THIRDPERSON_RENDER_OK distance=", player.get("ThirdPerson").get("ActualDistance"))
	scene.free()
	quit()
