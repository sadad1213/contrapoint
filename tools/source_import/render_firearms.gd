extends SceneTree

func _initialize() -> void:
	call_deferred("render_preview")

func render_preview() -> void:
	root.size = Vector2i(1280, 720)
	var scene := load("res://scenes/debug/weapon_preview.tscn").instantiate() as Node3D
	root.add_child(scene)
	var weapons := scene.get_node("Camera3D/Weapons")
	for weapon in weapons.get_children():
		weapon.set_process(false)
		if weapon.visible: weapon.call("Holster")
	var samples := {
		"Deagle": [["idle1", 0.2], ["shoot1", 0.1], ["reload", 1.0]],
		"USP": [["idle_unsil", 0.2], ["idle", 0.2], ["add_silencer", 1.5], ["reload", 1.3]],
		"M4A1": [["idle_unsil", 0.2], ["idle", 0.2], ["add_silencer", 1.0], ["reload_unsil", 1.5]],
		"AWP": [["awm_idle", 0.2], ["awm_fire", 0.7], ["awm_reload", 1.8]],
		"Scout": [["idle1", 0.2], ["shoot", 0.7], ["reload", 1.5]],
	}
	for weapon_name in samples:
		var weapon := weapons.get_node(NodePath(weapon_name))
		weapon.call("Equip")
		var player := weapon.find_child("AnimationPlayer", true, false) as AnimationPlayer
		for sample in samples[weapon_name]:
			player.stop(true)
			player.play(sample[0])
			player.seek(sample[1], true)
			await process_frame
			await process_frame
			await RenderingServer.frame_post_draw
			var path := "res://assets/weapons/%s/source/%s_%03d.png" % [weapon_name.to_lower(), sample[0], int(sample[1] * 100)]
			assert(root.get_texture().get_image().save_png(path) == OK)
			print("FIREARM_RENDER ", path)
		weapon.call("Holster")
	scene.free()
	quit()
