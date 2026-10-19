extends SceneTree

func _initialize() -> void:
	call_deferred("render_menu")

func render_menu() -> void:
	root.size = Vector2i(1280, 720)
	var menu: CanvasLayer = load("res://scenes/main_menu.tscn").instantiate()
	root.add_child(menu)
	for frame in 6:
		await process_frame
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png("user://main_menu_preview.png")
	menu.call("OpenMapSelection")
	await process_frame
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png("user://map_selection_preview.png")
	menu.call("SelectMap", 1)
	await process_frame
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png("user://map_selection_inferno_preview.png")
	menu.call("SelectMap", 2)
	await process_frame
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png("user://map_selection_kz_preview.png")
	menu.call("SelectSetupTab", 1)
	await process_frame
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png("user://map_selection_gameplay_preview.png")
	menu.call("SelectSetupTab", 2)
	await process_frame
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png("user://map_selection_bots_preview.png")
	menu.call("SelectSetupTab", 0)
	menu.call("SelectMode", "deathmatch")
	await process_frame
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png("user://map_selection_dm_preview.png")
	root.size = Vector2i(800, 600)
	for frame in 3:
		await process_frame
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png("user://map_selection_small_preview.png")
	print("MENU_PREVIEWS ", ProjectSettings.globalize_path("user://"))
	menu.queue_free()
	await process_frame
	quit()
