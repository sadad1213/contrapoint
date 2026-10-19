extends SceneTree

const BASE := "res://assets/maps/de_cpl_strike/"

func _initialize() -> void:
	call_deferred("render_map")

func source(p: Vector3) -> Vector3:
	return Vector3(p.x, p.z, -p.y) * 0.0254

func render_map() -> void:
	var breakables_only := "--breakables-only" in OS.get_cmdline_user_args()
	root.size = Vector2i(1280, 720)
	root.msaa_3d = Viewport.MSAA_4X
	var map := load(BASE + "de_cpl_strike.scn").instantiate() as Node3D
	root.add_child(map)
	var camera := Camera3D.new()
	camera.fov = 75
	camera.near = 0.025
	camera.far = 2000
	root.add_child(camera)
	camera.current = true
	for sample in [
		["t_spawn", Vector3(1240, -150, -95), Vector3(1050, -520, -120)],
		["ct_spawn", Vector3(-1800, -2000, -195), Vector3(-1600, -1500, -190)],
		["site_a", Vector3(-550, -2000, -95), Vector3(-350, -2250, -130)],
		["site_b", Vector3(-2300, 580, -60), Vector3(-1900, 260, -140)],
		["interior", Vector3(700, -2100, 65), Vector3(420, -2080, 35)],
		["wood", Vector3(-1920, -500, -85), Vector3(-1920, -280, -85)],
	]:
		if breakables_only:
			continue
		camera.position = source(sample[1])
		camera.look_at(source(sample[2]))
		for frame in 12:
			await process_frame
		await RenderingServer.frame_post_draw
		var picture := root.get_texture().get_image()
		assert(picture.save_png(BASE + "source/" + sample[0] + ".png") == OK)
		if sample[0] == "site_b":
			picture.resize(128, 72, Image.INTERPOLATE_LANCZOS)
			var thumb := Image.create(128, 128, false, Image.FORMAT_RGB8)
			thumb.blit_rect(picture, Rect2i(0, 0, 128, 72), Vector2i(0, 12))
			assert(thumb.save_png("res://assets/ui/css/de_cpl_strike.png") == OK)
		print("STRIKE_RENDER ", sample[0])
	# Close views of authored breakables before and after their collision is removed.
	for sample in [
		["boards", Vector3(-1920, -430, -80), Vector3(-1920, -280, -80), ["brush_15", "brush_16", "brush_17", "brush_18"]],
		["vent", Vector3(-1450, -968, -138), Vector3(-1364, -968, -138), ["brush_25"]],
	]:
		camera.position = source(sample[1])
		camera.look_at(source(sample[2]))
		for state in ["before", "after"]:
			if state == "after":
				for id: String in sample[3]:
					var brush := map.get_node(id) as Node3D
					brush.call("source_damage", 20, brush.global_position, Vector3.ZERO)
			for frame in 8:
				await process_frame
			await RenderingServer.frame_post_draw
			assert(root.get_texture().get_image().save_png(BASE + "source/" + sample[0] + "_" + state + ".png") == OK)
	camera.queue_free()
	map.queue_free()
	await process_frame
	if breakables_only:
		print("STRIKE_BREAKABLES_RENDER_OK")
		quit()
		return
	var game := load("res://scenes/maps/de_cpl_strike.tscn").instantiate() as Node3D
	root.add_child(game)
	for frame in 90:
		await physics_frame
	await RenderingServer.frame_post_draw
	assert(root.get_texture().get_image().save_png(BASE + "source/player_spawn.png") == OK)
	game.get_node("Player").call("SetMouseCaptured", false)
	game.queue_free()
	await process_frame
	print("STRIKE_RENDER_OK")
	quit()
