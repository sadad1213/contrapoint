extends SceneTree

func _initialize() -> void:
	call_deferred("render_console")

func render_console() -> void:
	root.size = Vector2i(1280, 720)
	var scene := load("res://scenes/maps/dust2.tscn").instantiate() as Node3D
	root.add_child(scene)
	for frame in 120:
		await physics_frame
	var console := scene.get_node("Player/DeveloperConsole")
	console.call("SetOpen", true)
	console.call("Execute", "sv_airaccelerate")
	console.call("Execute", "sv_airaccelerate 100")
	console.call("Execute", "sv_airaccelerate")
	var entry := console.get_node("Modal/ConsoleWindow/Command") as LineEdit
	entry.text = "sv_air"
	entry.emit_signal("text_changed", entry.text)
	await process_frame
	await RenderingServer.frame_post_draw
	assert(root.get_texture().get_image().save_png("user://console_preview.png") == OK)
	print("CONSOLE_PREVIEW " + ProjectSettings.globalize_path("user://console_preview.png"))
	scene.queue_free()
	await process_frame
	quit()
