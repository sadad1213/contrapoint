extends SceneTree

const BASE := "res://assets/maps/2000/"

func _initialize() -> void:
	call_deferred("render_map")

func source(p: Vector3) -> Vector3:
	return Vector3(p.x, p.z, -p.y) * 0.0254

func render_map() -> void:
	root.size = Vector2i(1280, 720)
	root.msaa_3d = Viewport.MSAA_4X
	var map := load(BASE + "2000.scn").instantiate() as Node3D
	root.add_child(map)
	var camera := Camera3D.new()
	camera.fov = 75
	camera.near = 0.025
	root.add_child(camera)
	camera.current = true
	for sample in [
		["overview", Vector3(-1380, 1450, 80), Vector3(-780, 760, -90)],
		["t_spawn", Vector3(-780, 1530, 25), Vector3(-780, 760, -100)],
		["ct_spawn", Vector3(-780, 0, 25), Vector3(-780, 760, -100)],
		["lower_arena", Vector3(-1460, 1250, -110), Vector3(-1460, 400, -110)],
	]:
		camera.position = source(sample[1])
		camera.look_at(source(sample[2]))
		for frame in 20:
			await process_frame
		await RenderingServer.frame_post_draw
		var picture := root.get_texture().get_image()
		assert(picture.save_png(BASE + "source/" + sample[0] + ".png") == OK)
		if sample[0] == "overview":
			picture.resize(128, 72, Image.INTERPOLATE_LANCZOS)
			var thumb := Image.create(128, 128, false, Image.FORMAT_RGB8)
			thumb.blit_rect(picture, Rect2i(0, 0, 128, 72), Vector2i(0, 12))
			assert(thumb.save_png("res://assets/ui/css/2000.png") == OK)
		print("ARENA_2000_RENDER ", sample[0])
	map.queue_free()
	camera.queue_free()
	await process_frame
	print("ARENA_2000_RENDER_OK")
	quit()
