extends SceneTree

func _initialize() -> void:
	call_deferred("run_checks")

func check(condition: bool, message: String) -> void:
	if not condition:
		push_error(message)
		quit(1)
		assert(condition, message)

func run_checks() -> void:
	var data: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://assets/maps/de_dust2/source/map_data.json"))
	var map := load("res://assets/maps/de_dust2/de_dust2.scn").instantiate() as Node3D
	root.add_child(map)
	var props: Array[RigidBody3D] = []
	var triangles := 0
	for mesh: MeshInstance3D in map.find_children("Chunk_*", "MeshInstance3D", true, false):
		triangles += mesh.mesh.surface_get_array_len(0) / 3
	check(triangles == int(data.stats.triangles), "Map geometry lost triangles")
	for id: String in data.entities:
		var entity: Dictionary = data.entities[id]
		var prop := map.get_node(id) as RigidBody3D
		check(prop != null and prop.has_method("source_damage"), "Missing physics prop " + id)
		check(entity.original_phy, "Missing original PHY hull " + id)
		var colliders := prop.find_children("*", "CollisionShape3D", false, false)
		check(colliders.size() == entity.physics_hulls.size(), "Incorrect hull count " + id)
		check(not prop.freeze, "Unexpected frozen prop " + id)
		check(prop.mass > 0 and prop.has_meta("source_surface"), "Missing mass/surface " + id)
		check(prop.find_children("Chunk_*", "MeshInstance3D", false, false).size() > 0, "Missing prop mesh " + id)
		# Isolate each body so walls and other props cannot absorb the test impulse.
		prop.collision_mask = 0
		prop.collision_layer = 0
		prop.gravity_scale = 0
		prop.position = Vector3(props.size() * 3, 100, 0)
		props.append(prop)
	check(props.size() == 75, "Expected all 75 original Dust 2 physics props")
	await physics_frame
	await physics_frame
	for prop: RigidBody3D in props:
		prop.call("source_damage", 30.0, prop.global_position, Vector3.RIGHT)
	await physics_frame
	await physics_frame
	for prop: RigidBody3D in props:
		check(prop.linear_velocity.x > 0.01, "Shot did not move " + str(prop.name))
	print("DUST2_PHYSICS_CHECKS_OK props=", props.size(), " triangles=", triangles)
	map.free()
	quit()
