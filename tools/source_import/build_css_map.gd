extends SceneTree

const MapOcclusion = preload("res://tools/source_import/map_occlusion.gd")

var BASE := "res://assets/maps/de_dust2/"
var map_id := "de_dust2"

func _initialize() -> void:
	call_deferred("build_map")

func build_map() -> void:
	var arguments := OS.get_cmdline_user_args()
	if arguments.size() > 0:
		map_id = arguments[0]
		BASE = "res://assets/maps/" + map_id + "/"
	var data: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(BASE + "source/map_data.json"))
	var shader := load("res://assets/maps/source_lightmap.gdshader") as Shader
	var two_sided := Shader.new()
	two_sided.code = shader.code.replace("cull_back", "cull_disabled")
	var lightmap := load(BASE + "lightmap_hdr.exr") as Texture2D
	assert(lightmap != null)
	var sound_bank: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://assets/audio/css/bank.json"))
	var materials := {}
	for name: String in data.materials:
		var entry: Dictionary = data.materials[name]
		for mode: String in ["lightmap", "vertex", "decal", "water"]:
			var mat := ShaderMaterial.new()
			mat.resource_name = name + "_" + mode
			mat.shader = two_sided if entry.nocull else shader
			if mode == "decal":
				mat.shader = load("res://assets/maps/source_modulate.gdshader" if entry.shader == "decalmodulate" else "res://assets/maps/source_decal.gdshader")
				mat.render_priority = 1
			elif mode == "water" and entry.has("normal_texture"):
				mat.shader = load("res://assets/maps/source_water.gdshader")
				mat.set_shader_parameter("normal_frames", load(BASE + entry.normal_texture))
				mat.set_shader_parameter("frame_count", int(entry.normal_frames))
				mat.set_shader_parameter("frame_rate", float(entry.normal_fps))
				mat.set_shader_parameter("fog_color", Vector3(entry.fog_color[0], entry.fog_color[1], entry.fog_color[2]))
				mat.set_shader_parameter("refract_tint", Vector3(entry.refract_tint[0], entry.refract_tint[1], entry.refract_tint[2]))
				mat.set_shader_parameter("reflect_tint", Vector3(entry.reflect_tint[0], entry.reflect_tint[1], entry.reflect_tint[2]))
				mat.set_shader_parameter("fog_start", float(entry.fog_start))
				mat.set_shader_parameter("fog_end", float(entry.fog_end))
				mat.set_shader_parameter("refract_amount", float(entry.refract_amount))
				if entry.has("reflection_texture"):
					mat.set_shader_parameter("reflection_map", load(BASE + entry.reflection_texture))
					mat.set_shader_parameter("has_reflection_map", true)
			elif entry.transparent:
				var transparent_shader := Shader.new()
				transparent_shader.code = shader.code.replace("cull_back", "cull_disabled" if entry.nocull else "cull_back").replace("ALBEDO =", "ALPHA = base.a * opacity;\n\tALBEDO =").replace("uniform float exposure", "uniform float opacity = 1.0;\nuniform float exposure")
				mat.shader = transparent_shader
				mat.set_shader_parameter("opacity", float(entry.get("alpha", 1.0)))
			if entry.texture != "":
				mat.set_shader_parameter("base_texture", load(BASE + entry.texture))
			else:
				mat.set_shader_parameter("base_texture", load("res://assets/maps/source_white.svg"))
			if entry.texture2 != "":
				mat.set_shader_parameter("second_texture", load(BASE + entry.texture2))
				mat.set_shader_parameter("blend_ground", true)
			mat.set_shader_parameter("lightmap_texture", lightmap)
			mat.set_shader_parameter("vertex_lighting", mode == "vertex")
			mat.set_shader_parameter("unlit", bool(entry.get("unlit", false)))
			mat.set_shader_parameter("alpha_test", bool(entry.alpha_test))
			mat.set_shader_parameter("alpha_cutoff", float(entry.alpha_cutoff))
			mat.set_shader_parameter("tint", Vector3(entry.color[0], entry.color[1], entry.color[2]))
			materials[name + "|" + mode] = mat
	var map := Node3D.new()
	map.name = "Inferno" if map_id == "de_inferno" else "Dust2" if map_id == "de_dust2" else map_id
	map.set_meta("source_map", map_id)
	for point: Dictionary in data.get("point_entities", []):
		if point.classname == "info_map_parameters":
			map.set_meta("source_bomb_radius", float(point.get("bombradius", 500)))
	if map_id.begins_with("kz_"):
		map.set_script(load("res://assets/maps/source_kz.gd"))
	var entity_nodes := {}
	for id: String in data.get("entities", {}):
		var entity: Dictionary = data.entities[id]
		var classname: String = entity.classname
		var object: Node3D
		if classname.begins_with("trigger_") or classname in ["func_buyzone", "func_bomb_target"]:
			object = Area3D.new()
			object.collision_layer = 8
			object.collision_mask = 1
			object.set_script(load("res://assets/maps/source_trigger.gd"))
		elif classname.begins_with("prop_physics") or classname in ["func_physbox", "func_physbox_multiplayer"]:
			object = RigidBody3D.new()
			object.set_script(load("res://assets/maps/source_prop.gd"))
			object.mass = float(entity.get("mass", 10.0)) * maxf(float(entity.get("massscale", 1)), 0.01) if float(entity.get("massscale", 1)) > 0 else float(entity.get("mass", 10.0))
			object.freeze = (int(entity.get("spawnflags", 0)) & 8) != 0
			object.sleeping = true
			object.continuous_cd = true
			object.gravity_scale = 20.32 / float(ProjectSettings.get_setting("physics/3d/default_gravity", 9.8))
			object.set_meta("source_surface", entity.get("surface", "concrete"))
		else:
			object = AnimatableBody3D.new()
			object.set_script(load("res://assets/maps/source_brush.gd"))
		object.name = id
		object.set("source_data", entity)
		if object is AnimatableBody3D:
			object.set("map_audio_root", BASE + "audio/")
		object.position = vector(entity.position)
		object.set_meta("source_class", classname)
		if entity.has("targetname"):
			object.set_meta("source_target", entity.targetname)
			object.add_to_group("source_target_" + entity.targetname, true)
		if classname == "func_bomb_target":
			object.add_to_group("source_bomb_target", true)
		if classname == "func_buyzone":
			object.add_to_group("source_buyzone", true)
		map.add_child(object)
		object.owner = map
		entity_nodes[id] = object
		if entity.has("animated_scene"):
			var model := (load(BASE + entity.animated_scene) as PackedScene).instantiate() as Node3D
			model.name = "AnimatedModel"
			model.rotation.y = deg_to_rad(float(str(entity.get("angles", "0 0 0")).split(" ")[1]) - 90)
			object.add_child(model)
			model.owner = map
		# Dust brushes describe a particle emission volume, not solid geometry.
		if entity.has("hulls") and not classname in ["func_illusionary", "func_lod", "func_dustmotes", "func_dustcloud", "func_smokevolume"] and entity.get("solidity", entity.get("Solidity", "0")) != "1":
			for hull: Array in entity.hulls:
				add_hull(object, hull, map, Vector3.ZERO)
		if entity.has("physics_hulls"):
			for hull: Array in entity.physics_hulls:
				add_hull(object, hull, map, Vector3.ZERO)
	var file := FileAccess.open(BASE + "source/geometry.bin", FileAccess.READ)
	var count := 0
	var triangle_count := 0
	for entry: Dictionary in data.groups:
		if data.get("entities", {}).get(entry.get("owner", ""), {}).has("animated_scene"):
			count += 1
			continue
		file.seek(int(entry.offset))
		var total := int(entry.vertices)
		var raw := file.get_buffer(total * 14 * 4).to_float32_array()
		assert(raw.size() == total * 14)
		var vertices := PackedVector3Array()
		var normals := PackedVector3Array()
		var uv := PackedVector2Array()
		var uv2 := PackedVector2Array()
		var colors := PackedColorArray()
		vertices.resize(total)
		normals.resize(total)
		uv.resize(total)
		uv2.resize(total)
		colors.resize(total)
		for i in total:
			var o := i * 14
			vertices[i] = Vector3(raw[o], raw[o+1], raw[o+2])
			normals[i] = Vector3(raw[o+3], raw[o+4], raw[o+5])
			uv[i] = Vector2(raw[o+6], raw[o+7])
			uv2[i] = Vector2(raw[o+8], raw[o+9])
			colors[i] = Color(raw[o+10], raw[o+11], raw[o+12], raw[o+13])
		var arrays := []
		arrays.resize(Mesh.ARRAY_MAX)
		arrays[Mesh.ARRAY_VERTEX] = vertices
		arrays[Mesh.ARRAY_NORMAL] = normals
		arrays[Mesh.ARRAY_TEX_UV] = uv
		arrays[Mesh.ARRAY_TEX_UV2] = uv2
		arrays[Mesh.ARRAY_COLOR] = colors
		var mesh := ArrayMesh.new()
		mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
		mesh.surface_set_material(0, materials[entry.material + "|" + entry.mode])
		var node := MeshInstance3D.new()
		node.name = "Chunk_%04d" % count
		node.mesh = mesh
		node.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		var parent: Node3D = entity_nodes.get(entry.get("owner", ""), map)
		if parent != map and not parent.has_meta("source_surface"):
			parent.set_meta("source_surface", data.materials[entry.material].get("surface", "concrete"))
		parent.add_child(node)
		if parent != map:
			node.position = -parent.position
		node.owner = map
		if entry.collision and parent == map and not (entry.mode == "vertex" and data.has("static_collisions")) and not (entry.mode == "lightmap" and data.has("world_hulls")):
			var body := StaticBody3D.new()
			body.name = "Collision"
			body.set_meta("source_surface", data.materials[entry.material].get("surface", sound_bank.surfaces.get(entry.material, "concrete")))
			var shape := CollisionShape3D.new()
			shape.shape = mesh.create_trimesh_shape()
			body.add_child(shape)
			node.add_child(body)
			body.owner = map
			shape.owner = map
		elif entry.collision and parent == map and entry.mode == "lightmap" and data.has("world_hulls"):
			for displacement: Dictionary in data.displacement_ranges:
				if int(displacement.group) != count:
					continue
				var body := StaticBody3D.new()
				body.name = "DisplacementCollision"
				body.set_meta("source_surface", data.materials[entry.material].get("surface", "concrete"))
				var shape := CollisionShape3D.new()
				var collision := ConcavePolygonShape3D.new()
				collision.set_faces(vertices.slice(int(displacement.first_vertex), int(displacement.first_vertex + displacement.vertices)))
				shape.shape = collision
				body.add_child(shape)
				node.add_child(body)
				body.owner = map
				shape.owner = map
		elif entry.collision and parent is RigidBody3D and not data.entities[entry.owner].has("physics_hulls") and not data.entities[entry.owner].has("hulls"):
			var collision := CollisionShape3D.new()
			collision.shape = mesh.create_convex_shape(true, false)
			collision.position = -parent.position
			parent.add_child(collision)
			collision.owner = map
			parent.set_meta("source_surface", data.materials[entry.material].get("surface", "concrete"))
		triangle_count += total / 3
		count += 1
	file.close()
	MapOcclusion.build(map, data)
	if data.has("world_hulls"):
		var world_collision := StaticBody3D.new()
		world_collision.name = "WorldBrushes"
		world_collision.set_meta("source_surfaces", PackedStringArray(data.world_surfaces))
		map.add_child(world_collision)
		world_collision.owner = map
		for hull: Array in data.world_hulls:
			add_hull(world_collision, hull, map, Vector3.ZERO)
	for entry: Dictionary in data.get("static_collisions", []):
		var body := StaticBody3D.new()
		body.name = "SourcePropCollision"
		body.set_meta("source_surface", entry.surface)
		body.set_meta("source_model", entry.model)
		map.add_child(body)
		body.owner = map
		for hull: Array in entry.hulls:
			add_hull(body, hull, map, Vector3.ZERO)
	var clips := StaticBody3D.new()
	clips.name = "PlayerClips"
	clips.collision_layer = 2
	map.add_child(clips)
	clips.owner = map
	for points: Array in data.clip_hulls:
		var shape := ConvexPolygonShape3D.new()
		var hull := PackedVector3Array()
		for p: Array in points:
			hull.append(Vector3(p[0], p[1], p[2]))
		shape.points = hull
		var collider := CollisionShape3D.new()
		collider.shape = shape
		clips.add_child(collider)
		collider.owner = map
	var water_index := 0
	for hull: Array in data.get("ladder_hulls", []):
		var ladder := Area3D.new()
		ladder.name = "Ladder_%02d" % map.get_child_count()
		ladder.collision_layer = 16
		ladder.collision_mask = 0
		ladder.monitoring = false
		ladder.add_to_group("source_ladder", true)
		map.add_child(ladder)
		ladder.owner = map
		add_hull(ladder, hull, map, Vector3.ZERO)
		var bounds := AABB(vector(hull[0]), Vector3.ZERO)
		for p: Array in hull:
			bounds = bounds.expand(vector(p))
		ladder.set_meta("ladder_center", bounds.get_center())
		ladder.set_meta("ladder_normal", Vector3.RIGHT if bounds.size.x < bounds.size.z else Vector3.BACK)
	for hull: Array in data.get("water_hulls", []):
		var water := Area3D.new()
		water.name = "Water_%02d" % water_index
		water.collision_layer = 4
		water.collision_mask = 0
		water.monitoring = false
		water.add_to_group("source_water", true)
		map.add_child(water)
		water.owner = map
		add_hull(water, hull, map, Vector3.ZERO)
		water_index += 1
	# Props parented to moving doors and fans retain their authored world transform.
	for id: String in entity_nodes:
		var entity: Dictionary = data.entities[id]
		if entity.has("parentname"):
			for target_id: String in entity_nodes:
				if data.entities[target_id].get("targetname", "") == entity.parentname:
					var child: Node3D = entity_nodes[id]
					var old_position := child.position
					child.get_parent().remove_child(child)
					child.owner = null
					entity_nodes[target_id].add_child(child)
					child.position = old_position - entity_nodes[target_id].position
					child.owner = map
	build_ropes(data.get("point_entities", []), map)
	build_effects(data.get("effects", []), data.get("point_entities", []), map)
	build_weapon_pickups(data.get("point_entities", []), map)
	if data.has("point_entities"):
		for entity: Dictionary in data.get("point_entities", []):
			if not entity.classname in ["info_teleport_destination", "point_teleport", "path_track", "ambient_generic", "logic_timer", "logic_auto"]:
				continue
			var point := Node3D.new()
			point.name = "Point_" + str(entity.get("hammerid", map.get_child_count()))
			point.set_script(load("res://assets/maps/source_point.gd"))
			point.set("source_data", entity)
			point.set("map_audio_root", BASE + "audio/")
			point.position = source_vector(entity.get("origin", "0 0 0"))
			var room: Dictionary = data.sky_room
			var raw := str(entity.get("origin", "0 0 0")).split(" ", false)
			var raw_position := Vector3(float(raw[0]), float(raw[1]), float(raw[2]))
			var sky_bounds := AABB(vector(room.mins), vector(room.maxs) - vector(room.mins))
			if sky_bounds.has_point(raw_position):
				point.position = (point.position - source_vector("%s %s %s" % room.origin)) * float(room.scale)
			if entity.has("targetname"):
				point.add_to_group("source_target_" + entity.targetname, true)
			map.add_child(point)
			point.owner = map
	for entity: Dictionary in data.get("point_entities", []):
		if entity.classname == "func_fish_pool":
			var pool := Node3D.new()
			pool.name = "FishPool"
			pool.set_script(load("res://assets/maps/source_fish.gd"))
			pool.set("count", int(entity.get("fish_count", 10)))
			pool.set("radius", float(entity.get("max_range", 150)) * 0.0254 * 0.7)
			pool.set("model", load(BASE + "fish/goldfish.glb"))
			pool.position = source_vector(entity.origin)
			map.add_child(pool)
			pool.owner = map
	if data.has("soundscapes"):
		var soundscapes := Node3D.new()
		soundscapes.name = "Soundscapes"
		soundscapes.set_script(load("res://assets/maps/source_soundscapes.gd"))
		soundscapes.set("definitions", data.soundscapes)
		soundscapes.set("points", data.point_entities)
		soundscapes.set("audio_root", BASE + "audio/")
		map.add_child(soundscapes)
		soundscapes.owner = map
	for entity: Dictionary in data.get("point_entities", []):
		if entity.classname != "phys_ragdollconstraint":
			continue
		for id: String in entity_nodes:
			if data.entities[id].get("targetname", "") == entity.get("attach1", "") and entity_nodes[id] is RigidBody3D:
				var joint := PinJoint3D.new()
				joint.name = "Constraint_" + str(entity.hammerid)
				joint.position = source_vector(entity.origin)
				map.add_child(joint)
				joint.node_a = joint.get_path_to(entity_nodes[id])
				joint.owner = map
	var sky_mat := ShaderMaterial.new()
	sky_mat.shader = load("res://assets/maps/source_sky.gdshader")
	for pair in [["front", "ft"], ["back", "bk"], ["left", "lf"], ["right", "rt"], ["up", "up"], ["down", "dn"]]:
		sky_mat.set_shader_parameter(pair[0] + "_tex", load(BASE + data.sky[pair[1]]))
	var sky := Sky.new()
	sky.sky_material = sky_mat
	var env := Environment.new()
	env.background_mode = Environment.BG_SKY
	env.sky = sky
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.95, 0.89, 0.76)
	env.ambient_light_energy = 0.5
	env.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	if data.has("fog") and data.fog.get("fogenable", "0") == "1":
		env.fog_enabled = true
		env.fog_mode = Environment.FOG_MODE_DEPTH
		env.fog_light_color = Color(float(data.fog.fogcolor.split(" ")[0])/255, float(data.fog.fogcolor.split(" ")[1])/255, float(data.fog.fogcolor.split(" ")[2])/255)
		env.fog_depth_begin = float(data.fog.fogstart) * 0.0254
		env.fog_depth_end = float(data.fog.fogend) * 0.0254
		env.fog_density = float(data.fog.get("fogmaxdensity", 1))
		env.fog_sky_affect = 0.0
	var world := WorldEnvironment.new()
	world.name = "WorldEnvironment"
	world.environment = env
	map.add_child(world)
	world.owner = map
	for i in data.spawns.size():
		var spawn: Dictionary = data.spawns[i]
		var marker := Marker3D.new()
		marker.name = "Spawn_%s_%02d" % [spawn.team, i]
		marker.position = Vector3(spawn.position[0], spawn.position[1], spawn.position[2])
		marker.rotation.y = deg_to_rad(float(spawn.yaw))
		map.add_child(marker)
		marker.owner = map
	var packed := PackedScene.new()
	# Reparenting detaches descendants' owners; restore the complete packed tree.
	restore_owners(map, map)
	assert(packed.pack(map) == OK)
	assert(ResourceSaver.save(packed, BASE + map_id + ".scn") == OK)
	print("MAP_BUILD_COMPLETE chunks=", count, " triangles=", triangle_count)
	map.free()
	quit()

func build_weapon_pickups(points: Array, map: Node3D) -> void:
	var supported: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://assets/physics/css_weapon_solids.json"))
	var pickup_script = load("res://weapons/WeaponPickup.cs")
	var omitted := {}
	for index in points.size():
		var entity: Dictionary = points[index]
		var classname: String = entity.classname
		if not classname.begins_with("weapon_"):
			continue
		if not supported.has(classname):
			omitted[classname] = int(omitted.get(classname, 0)) + 1
			continue
		var pickup = pickup_script.new()
		pickup.name = "MapWeapon_%d" % index
		pickup.set("WeaponClass", classname)
		pickup.set("Magazine", -1)
		pickup.set("ExtraAmmo", -1)
		pickup.position = source_vector(entity.get("origin", "0 0 0"))
		var angles := str(entity.get("angles", "0 0 0")).split(" ", false)
		pickup.quaternion = Quaternion(Vector3.UP, deg_to_rad(float(angles[1]))) * Quaternion(Vector3.RIGHT, deg_to_rad(float(angles[0]))) * Quaternion(Vector3.FORWARD, deg_to_rad(float(angles[2])))
		map.add_child(pickup)
		pickup.owner = map
	if not omitted.is_empty():
		print("MAP_UNSUPPORTED_WEAPONS ", JSON.stringify(omitted))

func vector(value: Array) -> Vector3:
	return Vector3(value[0], value[1], value[2])

func restore_owners(node: Node, owner_node: Node) -> void:
	for child: Node in node.get_children():
		child.owner = owner_node
		# Imported scenes keep ownership of their internal skeleton and animation.
		if child.scene_file_path == "":
			restore_owners(child, owner_node)

func source_vector(value: String) -> Vector3:
	var parts := value.split(" ", false)
	return Vector3(float(parts[0]), float(parts[2]), -float(parts[1])) * 0.0254

func add_hull(parent: Node3D, points: Array, owner_node: Node, offset: Vector3) -> void:
	var shape := ConvexPolygonShape3D.new()
	shape.margin = 0.001
	var vertices := PackedVector3Array()
	for p: Array in points:
		vertices.append(vector(p) - offset)
	shape.points = vertices
	var collider := CollisionShape3D.new()
	collider.shape = shape
	parent.add_child(collider)
	collider.owner = owner_node

func build_ropes(entities: Array, map: Node3D) -> void:
	var targets := {}
	for entity: Dictionary in entities:
		if entity.has("targetname"):
			targets[entity.targetname] = entity
	var material := StandardMaterial3D.new()
	material.albedo_color = Color(0.06, 0.055, 0.045)
	material.roughness = 1.0
	for entity: Dictionary in entities:
		var next_key: String = entity.get("nextkey", entity.get("NextKey", ""))
		if not entity.classname in ["move_rope", "keyframe_rope"] or not targets.has(next_key):
			continue
		var start := source_vector(entity.origin)
		var finish := source_vector(targets[next_key].origin)
		var slack := float(entity.get("slack", entity.get("Slack", 0))) * 0.0254
		var mesh := ImmediateMesh.new()
		mesh.surface_begin(Mesh.PRIMITIVE_TRIANGLES, material)
		var radius := maxf(float(entity.get("width", entity.get("Width", 1))) * 0.0254 * 0.5, 0.006)
		for i in 24:
			var t0 := i / 24.0
			var t1 := (i + 1) / 24.0
			var a := start.lerp(finish, t0) + Vector3.DOWN * (4 * t0 * (1-t0) * slack * 0.5)
			var b := start.lerp(finish, t1) + Vector3.DOWN * (4 * t1 * (1-t1) * slack * 0.5)
			var direction := (b-a).normalized()
			var right := direction.cross(Vector3.UP if absf(direction.y) < 0.99 else Vector3.RIGHT).normalized()
			var up := right.cross(direction).normalized()
			for side in 6:
				var n0 := right * cos(side * TAU/6) + up * sin(side * TAU/6)
				var n1 := right * cos((side+1) * TAU/6) + up * sin((side+1) * TAU/6)
				for p: Vector3 in [a+n0*radius, b+n0*radius, a+n1*radius, a+n1*radius, b+n0*radius, b+n1*radius]:
					mesh.surface_set_normal(n0)
					mesh.surface_add_vertex(p)
		mesh.surface_end()
		var instance := MeshInstance3D.new()
		instance.name = "Rope_" + entity.hammerid
		instance.mesh = mesh
		map.add_child(instance)
		instance.owner = map

func build_effects(effects: Array, points: Array, map: Node3D) -> void:
	var targets := {}
	for point: Dictionary in points:
		if point.has("targetname") and point.has("origin"):
			targets[point.targetname] = source_vector(point.origin)
	for entry: Dictionary in effects:
		var entity: Dictionary = entry.entity
		var colors: PackedStringArray = str(entity.get("rendercolor", "255 255 255")).split(" ")
		var tint := Color(float(colors[0])/255, float(colors[1])/255, float(colors[2])/255, float(entity.get("renderamt", 255))/255)
		var node := MeshInstance3D.new()
		var quad := QuadMesh.new()
		if entity.classname == "env_sprite":
			node.name = "Glow_" + entity.hammerid
			quad.size = Vector2(entry.width, entry.height) * float(entity.get("scale", 1)) * 0.0254
			var material := StandardMaterial3D.new()
			material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
			material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
			material.blend_mode = BaseMaterial3D.BLEND_MODE_ADD
			material.billboard_mode = BaseMaterial3D.BILLBOARD_ENABLED
			material.albedo_color = tint
			material.albedo_texture = load(BASE + entry.texture)
			node.material_override = material
			node.position = source_vector(entity.origin)
		else:
			node.name = "Smoke_" + entity.hammerid
			var start: Vector3 = targets[entity.lightningstart]
			var finish: Vector3 = targets[entity.lightningend]
			quad.size = Vector2(float(entity.get("boltwidth", 14)) * 0.0254, start.distance_to(finish))
			node.set_script(load("res://assets/maps/source_beam.gd"))
			node.set("start", start)
			node.set("finish", finish)
			var material := ShaderMaterial.new()
			material.shader = load("res://assets/maps/source_beam.gdshader")
			material.set_shader_parameter("source_texture", load(BASE + entry.texture))
			material.set_shader_parameter("frame_count", int(entry.frames))
			material.set_shader_parameter("frame_rate", float(entity.get("framerate", 20)))
			material.set_shader_parameter("scroll_rate", float(entity.get("texturescroll", 5)) * 0.01)
			material.set_shader_parameter("tint", tint)
			node.material_override = material
		node.mesh = quad
		node.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		map.add_child(node)
		node.owner = map
