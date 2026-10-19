extends SceneTree

func _initialize() -> void:
	call_deferred("verify")

func verify() -> void:
	var map := load("res://assets/maps/de_cache/de_cache.scn").instantiate() as Node3D
	root.add_child(map)
	await physics_frame
	await physics_frame
	var dust := map.get_node("brush_9") as CollisionObject3D
	assert(dust.get_meta("source_class") == "func_dustmotes")
	var blocked := "--expect-blocked" in OS.get_cmdline_user_args()
	var space := map.get_world_3d().direct_space_state
	# User's position: Source units along Godot's axes, as shown by cl_showpos.
	var center := Vector3(368.97, -124.11, 650.25) * 0.0254
	var collider := CharacterBody3D.new()
	var shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(32, 72, 32) * 0.0254
	box.margin = 0.001
	shape.shape = box
	shape.position.y = box.size.y / 2
	collider.add_child(shape)
	collider.collision_mask = 3
	root.add_child(collider)
	# Probe each side; some volume boundaries also coincide with real walls.
	var crossings := 0
	var dust_hits := 0
	for boundary: Vector3 in [Vector3(6.4516, center.y, center.z), Vector3(14.732, center.y, center.z), Vector3(center.x, center.y, 11.3792), Vector3(center.x, center.y, 21.6154)]:
		var outward := (boundary - center).normalized()
		var start := boundary + outward * 0.8
		start.y = -160 * 0.0254 + 0.04
		collider.position = start
		await physics_frame
		await physics_frame
		var motion := -outward * 1.6
		motion.y = 0
		var collision := collider.move_and_collide(motion)
		print("DUST_BOUNDARY ", boundary, " hit=", collision.get_collider().get_path() if collision else "none", " travel=", collider.position - start)
		if collision != null and collision.get_collider() == dust:
			dust_hits += 1
		if collider.position.distance_to(start + motion) < 0.01:
			crossings += 1
	if blocked:
		assert(dust_hits > 0, "baseline must reproduce the dust-volume wall")
		print("CACHE_DUST_BUG_REPRODUCED hits=", dust_hits)
	else:
		assert(dust.find_children("*", "CollisionShape3D", true, false).is_empty(), "dust volume must not create solid shapes")
		assert(crossings > 0, "player hull must cross the old volume boundary")
		var hits := 0
		for direction in [Vector3.LEFT, Vector3.RIGHT, Vector3.FORWARD, Vector3.BACK, Vector3.UP]:
			var query := PhysicsRayQueryParameters3D.create(center, center + direction * 20, 3)
			query.exclude = [collider.get_rid()]
			var hit := space.intersect_ray(query)
			assert(hit.is_empty() or hit.collider != dust, "bullets must not hit the dust volume")
			if not hit.is_empty():
				hits += 1
		assert(hits > 0, "real room walls remain solid")
		assert(map.get_node("brush_10") is Area3D and map.get_node("brush_11") is Area3D, "both bomb zones remain areas")
		print("CACHE_DUST_VOLUME_CHECKS_OK crossings=", crossings, " room_wall_hits=", hits)
	collider.queue_free()
	map.queue_free()
	await process_frame
	quit()
