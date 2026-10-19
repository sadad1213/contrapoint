extends SceneTree

const BASE := "res://assets/maps/de_cache/"

func _initialize() -> void:
	call_deferred("render_map")

func source(p: Vector3) -> Vector3:
	return Vector3(p.x, p.z, -p.y) * 0.0254

func render_map() -> void:
	root.size = Vector2i(1280, 720)
	root.msaa_3d = Viewport.MSAA_4X
	var map := load(BASE + "de_cache.scn").instantiate() as Node3D
	root.add_child(map)
	var camera := Camera3D.new()
	camera.fov = 75
	camera.near = 0.025
	camera.far = 2000
	root.add_child(camera)
	camera.current = true
	for sample in [
		["t_spawn", Vector3(3050, 0, -80), Vector3(2300, 0, -80)],
		["ct_spawn", Vector3(-1400, 650, -80), Vector3(-1400, 100, -80)],
		["site_a", Vector3(-350, 1450, 0), Vector3(-240, 1800, -75)],
		["site_b", Vector3(-260, -1000, -30), Vector3(0, -1300, -100)],
		["mid", Vector3(600, 700, 40), Vector3(600, 100, -90)],
	]:
		camera.position = source(sample[1])
		camera.look_at(source(sample[2]))
		for frame in 20:
			await process_frame
		await RenderingServer.frame_post_draw
		var picture := root.get_texture().get_image()
		assert(picture.save_png(BASE + "source/" + sample[0] + ".png") == OK)
		if sample[0] == "site_a":
			picture.resize(128, 72, Image.INTERPOLATE_LANCZOS)
			var thumb := Image.create(128, 128, false, Image.FORMAT_RGB8)
			thumb.blit_rect(picture, Rect2i(0, 0, 128, 72), Vector2i(0, 12))
			assert(thumb.save_png("res://assets/ui/css/de_cache.png") == OK)
		print("CACHE_RENDER ", sample[0])
	map.queue_free()
	camera.queue_free()
	await process_frame
	print("CACHE_RENDER_OK")
	quit()
