extends SceneTree

# Small rendered regression: neutral modulation must not darken a surface.
# Run with --rendering-method mobile (or forward_plus) --script THIS_FILE.
var failures := 0

func _initialize() -> void:
	call_deferred("run")

func frames() -> void:
	for i in 4:
		await process_frame
	await RenderingServer.frame_post_draw

func check_pixel(actual: Color, expected: Color, label: String) -> void:
	var error := maxf(absf(actual.r - expected.r), maxf(absf(actual.g - expected.g), absf(actual.b - expected.b)))
	print("MODULATION_CHECK ", label, " error=", error)
	if error > 0.009:
		failures += 1
		push_error(label + ": neutral modulation changed the surface")

func run() -> void:
	root.mode = Window.MODE_WINDOWED
	root.size = Vector2i(640, 360)
	root.content_scale_mode = Window.CONTENT_SCALE_MODE_DISABLED
	var camera := Camera3D.new()
	camera.position.z = 3
	root.add_child(camera)
	camera.current = true
	var world := WorldEnvironment.new()
	world.environment = Environment.new()
	world.environment.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	root.add_child(world)
	var wall := MeshInstance3D.new()
	wall.mesh = QuadMesh.new()
	wall.mesh.size = Vector2(8, 5)
	var background := StandardMaterial3D.new()
	background.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	background.albedo_color = Color(0.5, 0.4, 0.3)
	wall.material_override = background
	root.add_child(wall)
	await frames()
	var original := root.get_texture().get_image()
	var image := Image.create(16, 16, false, Image.FORMAT_RGBA8)
	image.fill(Color(0.5, 0.5, 0.5, 1))
	var neutral := ImageTexture.create_from_image(image)
	var mark := MeshInstance3D.new()
	mark.position.z = 0.002
	mark.mesh = QuadMesh.new()
	mark.mesh.size = Vector2.ONE
	root.add_child(mark)
	var pixel := Vector2i(camera.unproject_position(mark.position))
	var material := ShaderMaterial.new()
	material.shader = load("res://weapons/effects/impact_mark.gdshader")
	material.set_shader_parameter("mark_texture", neutral)
	mark.material_override = material
	await frames()
	check_pixel(root.get_texture().get_image().get_pixelv(pixel), original.get_pixelv(pixel), "bullet neutral border")
	image.fill(Color.BLACK)
	material.set_shader_parameter("mark_texture", ImageTexture.create_from_image(image))
	material.set_shader_parameter("opacity", 0.0)
	await frames()
	check_pixel(root.get_texture().get_image().get_pixelv(pixel), original.get_pixelv(pixel), "faded bullet hole")
	material = ShaderMaterial.new()
	material.shader = load("res://assets/maps/source_modulate.gdshader")
	material.set_shader_parameter("base_texture", neutral)
	mark.material_override = material
	await frames()
	check_pixel(root.get_texture().get_image().get_pixelv(pixel), original.get_pixelv(pixel), "map decal neutral border")
	material = ShaderMaterial.new()
	material.shader = load("res://weapons/effects/csso_refract.gdshader")
	material.set_shader_parameter("sheet", neutral)
	mark.material_override = material
	mark.set_instance_shader_parameter("tint", Color.WHITE)
	await frames()
	check_pixel(root.get_texture().get_image().get_pixelv(pixel), original.get_pixelv(pixel), "screen refraction brightness")
	# Exercise the actual C# particle material generator, including its blend_mul variant.
	var ceiling := StaticBody3D.new()
	ceiling.position.y = 4
	var collision := CollisionShape3D.new()
	collision.shape = BoxShape3D.new()
	collision.shape.size = Vector3(4, 0.2, 4)
	ceiling.add_child(collision)
	root.add_child(ceiling)
	await physics_frame
	await physics_frame
	var effect: Node3D = load("res://weapons/effects/CssoExplosion.cs").new()
	root.add_child(effect)
	await physics_frame
	await physics_frame
	effect.set_process(false)
	var found := false
	for child in effect.get_children():
		if child is MeshInstance3D and child.material_override.shader.code.contains("blend_mul"):
			mark.material_override = child.material_override
			mark.set_instance_shader_parameter("tint", Color(1, 1, 1, 0))
			found = true
			break
	effect.visible = false
	if not found:
		failures += 1
		push_error("No multiplicative explosion material exercised")
	else:
		await frames()
		check_pixel(root.get_texture().get_image().get_pixelv(pixel), original.get_pixelv(pixel), "faded multiplicative particle")
	print("MOBILE_MATERIAL_CHECKS failures=", failures)
	quit(1 if failures else 0)
