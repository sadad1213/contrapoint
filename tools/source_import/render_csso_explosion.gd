extends SceneTree

func _initialize() -> void:
	call_deferred("render_effect")

func capture(path: String) -> void:
	await process_frame
	await process_frame
	await RenderingServer.frame_post_draw
	assert(root.get_texture().get_image().save_png(path) == OK)
	print("CSSO_EXPLOSION_RENDER ", path)

func render_effect() -> void:
	seed(12345)
	root.size = Vector2i(1280, 720)
	AudioServer.set_bus_mute(0, true)
	var scene := load("res://scenes/maps/dust2.tscn").instantiate() as Node3D
	root.add_child(scene)
	var player := scene.get_node("Player")
	player.set_physics_process(false)
	player.call("SetMouseCaptured", false)
	var camera := player.get_node("View/Camera3D") as Camera3D
	var weapons := camera.get_node("Weapons")
	weapons.visible = false
	for weapon in weapons.get_children():
		weapon.set_process(false)
		weapon.set_physics_process(false)
	await physics_frame
	await physics_frame
	var burst: Node3D = load("res://weapons/effects/GrenadeBurst.cs").new()
	scene.add_child(burst)
	burst.global_position = camera.global_position - camera.global_basis.z * 7 + Vector3.DOWN * 1.3
	burst.set_process(false)
	var effect := burst.get_child(1) as Node3D
	var interior := "--interior" in OS.get_cmdline_user_args()
	if interior:
		var ceiling := StaticBody3D.new()
		var collision := CollisionShape3D.new()
		var box := BoxShape3D.new()
		box.size = Vector3(4, 0.2, 4)
		collision.shape = box
		ceiling.add_child(collision)
		scene.add_child(ceiling)
		ceiling.global_position = effect.global_position + Vector3.UP * 4
	effect.set_process(false)
	effect.set_physics_process(false)
	await physics_frame
	await physics_frame
	effect.call("_PhysicsProcess", 1.0 / 66)
	var last := 0.0
	for time in [0.06, 0.16, 0.4, 0.9, 1.8, 3.5]:
		while last < time - 0.00001:
			var step: float = min(1.0 / 120, time - last)
			effect.call("_Process", step)
			burst.call("_Process", step)
			last += step
		await capture("res://.godot/csso_explosion_%s_%04d.png" % ["interior" if interior else "dirt", int(time * 1000)])
	scene.free()
	quit()
