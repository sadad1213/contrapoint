extends SceneTree

func _initialize() -> void:
	call_deferred("render_map")

func source(p: Vector3) -> Vector3:
	return Vector3(p.x, p.z, -p.y) * 0.0254

func render_map() -> void:
	root.size = Vector2i(1280, 720)
	root.msaa_3d = Viewport.MSAA_4X
	var map := load("res://assets/maps/kz_jayverb000/kz_jayverb000.scn").instantiate() as Node3D
	root.add_child(map)
	map.get_node("KzTimer").hide()
	var camera := Camera3D.new()
	camera.fov = 75
	camera.near = 0.05
	camera.far = 2000
	root.add_child(camera)
	camera.current = true
	for sample in [
		["spawn", Vector3(-1144, 720, 20), Vector3(-864, 1092, 0)],
		["start", Vector3(700, 1350, 240), Vector3(892, 1535, 186)],
		["courtyard", Vector3(600, 300, 360), Vector3(-800, -800, 150)],
		["bhop", Vector3(-400, -3100, 260), Vector3(-650, -4000, 110)],
		["finish", Vector3(-850, -2600, 290), Vector3(-1022, -2752, 232)],
		["graffiti", Vector3(-600, -500, 220), Vector3(-1100, -500, 200)],
	]:
		camera.position = source(sample[1])
		camera.look_at(source(sample[2]))
		for frame in 8:
			await process_frame
		await RenderingServer.frame_post_draw
		var picture := root.get_texture().get_image()
		picture.save_png("res://assets/maps/kz_jayverb000/source/" + sample[0] + ".png")
		if sample[0] == "courtyard":
			picture.resize(128, 72, Image.INTERPOLATE_LANCZOS)
			var thumb := Image.create(128, 128, false, Image.FORMAT_RGB8)
			thumb.blit_rect(picture, Rect2i(0, 0, 128, 72), Vector2i(0, 12))
			thumb.save_png("res://assets/ui/css/kz_jayverb000.png")
		print("KZ_RENDER ", sample[0])
	map.queue_free()
	await process_frame
	quit()
