extends SceneTree

func _initialize() -> void:
	call_deferred("render_preview")

func render_preview() -> void:
	root.size = Vector2i(1280, 720)
	var scene := load("res://scenes/debug/weapon_preview.tscn").instantiate() as Node3D
	root.add_child(scene)
	var event := InputEventKey.new()
	event.physical_keycode = KEY_1
	event.pressed = true
	root.push_input(event)
	var weapon := scene.get_node("Camera3D/Weapons/AK47")
	weapon.set_process(false)
	var player := weapon.find_child("AnimationPlayer", true, false) as AnimationPlayer
	player.callback_mode_process = AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
	for sample in [["ak47_idle", 0.2], ["ak47_draw", 0.5], ["ak47_reload", 0.7], ["ak47_reload", 1.5], ["ak47_fire1", 0.1]]:
		player.play(sample[0])
		player.seek(sample[1], true)
		await process_frame
		await process_frame
		await RenderingServer.frame_post_draw
		var output := "res://assets/weapons/ak47/source/%s_%03d.png" % [sample[0], int(sample[1] * 100)]
		var result := root.get_texture().get_image().save_png(output)
		assert(result == OK, "Could not save render")
		print("AK47_RENDER ", output)
	scene.queue_free()
	quit()
