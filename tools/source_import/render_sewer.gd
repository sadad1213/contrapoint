extends SceneTree

const BASE := "res://assets/maps/hns_sewer/"

func _initialize() -> void:
	call_deferred("render_map")

func source(p: Vector3) -> Vector3:
	return Vector3(p.x, p.z, -p.y) * 0.0254

func render_map() -> void:
	root.size = Vector2i(1280, 720)
	root.msaa_3d = Viewport.MSAA_4X
	var map := load(BASE + "hns_sewer.scn").instantiate() as Node3D
	root.add_child(map)
	var camera := Camera3D.new()
	camera.fov = 80
	camera.near = 0.025
	root.add_child(camera)
	camera.current = true
	for sample in [
		["courtyard", Vector3(400, 700, 280), Vector3(-400, 1350, 190)],
		["spawn", Vector3(-673, 1326, 195), Vector3(-400, 1000, 180)],
		["sewer", Vector3(-600, 1152, -130), Vector3(300, 1152, -150)],
		["radio", Vector3(-210, 832, -65), Vector3(-256, 832, -65)],
	]:
		camera.position = source(sample[1])
		camera.look_at(source(sample[2]))
		for frame in 12:
			await process_frame
		await RenderingServer.frame_post_draw
		var picture := root.get_texture().get_image()
		assert(picture.save_png(BASE + "source/" + sample[0] + ".png") == OK)
		if sample[0] == "courtyard":
			picture.resize(128, 72, Image.INTERPOLATE_LANCZOS)
			var thumb := Image.create(128, 128, false, Image.FORMAT_RGB8)
			thumb.blit_rect(picture, Rect2i(0, 0, 128, 72), Vector2i(0, 12))
			assert(thumb.save_png("res://assets/ui/css/hns_sewer.png") == OK)
		print("SEWER_RENDER ", sample[0])
	map.queue_free()
	camera.queue_free()
	await process_frame
	var game := load("res://scenes/maps/hns_sewer.tscn").instantiate() as Node3D
	root.add_child(game)
	game.get_node("Player").call("JoinTeam", 2)
	game.get_node("Player").call("JoinClass", 1)
	for frame in 20:
		await process_frame
	await RenderingServer.frame_post_draw
	assert(root.get_texture().get_image().save_png(BASE + "source/player_spawn.png") == OK)
	game.get_node("Player").call("SetMouseCaptured", false)
	game.queue_free()
	await process_frame
	await process_frame
	print("SEWER_RENDER_OK")
	quit()
