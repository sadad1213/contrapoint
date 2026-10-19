extends SceneTree

func _initialize() -> void:
	call_deferred("render_c4")

func render_c4() -> void:
	root.size = Vector2i(1280, 720)
	var scene = load("res://scenes/maps/dust2.tscn").instantiate()
	root.add_child(scene)
	current_scene = scene
	await process_frame
	await process_frame
	var p = scene.get_node("Player")
	p.call("JoinTeam", 2)
	p.call("JoinClass", 1)
	p.set_physics_process(false)
	p.call("SetMouseCaptured", true)
	var inventory = p.get_node("View/Camera3D/Weapons")
	var c4 = inventory.call("AddWeapon", load("res://weapons/c4_view.tscn"), true, true)
	c4.set_process(false)
	var animation = c4.find_child("AnimationPlayer", true, false)
	animation.stop()
	animation.play("idle1")
	animation.seek(0, true)
	for _i in 8: await process_frame
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png("res://.godot/c4-material-fixed.png")
	var round_state = root.get_node("RoundState")
	var de = root.get_node("DefuseState")
	de.set_physics_process(false)
	round_state.call("Receive", "defuse", false, 100.0, true, false, 1, 0, 0)
	var state: Dictionary = de.call("Capture")
	state.phase = 2
	state.round = 1
	state.defuser = p.get("NetworkPeerId")
	state.defuse = 2.5
	state.length = 5.0
	p.set("HasDefuser", true)
	de.call("Receive", state)
	for _i in 3: await process_frame
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png("res://.godot/de-progress-no-label.png")
	print("C4_RENDER_COMPLETE")
	quit()
