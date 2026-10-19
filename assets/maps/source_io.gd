extends RefCounted

# Source outputs retain activators through delays and per-connection fire limits.
static func emit(source: Node, data: Dictionary, event: String, activator: Node = null) -> void:
	if preload("res://assets/maps/source_network.gd").is_client(source):
		return
	for key: String in data:
		if key.to_lower() != event.to_lower():
			continue
		var outputs: Array = data[key] if data[key] is Array else [data[key]]
		for i in outputs.size():
			var parts := str(outputs[i]).replace(char(27), ",").split(",")
			if parts.size() < 4:
				continue
			var counts: Dictionary = source.get_meta("source_output_counts", {})
			var id := key + str(i)
			var used := int(counts.get(id, 0))
			var limit := int(parts[4]) if parts.size() > 4 else -1
			if limit >= 0 and used >= limit:
				continue
			counts[id] = used + 1
			source.set_meta("source_output_counts", counts)
			deliver(source, parts, activator)

static func deliver(source: Node, parts: PackedStringArray, activator: Node) -> void:
	if float(parts[3]) > 0:
		await source.get_tree().create_timer(float(parts[3]), false).timeout
	if not is_instance_valid(source) or not source.is_inside_tree():
		return
	var targets: Array = []
	if parts[0] == "!self":
		targets = [source]
	elif parts[0] == "!activator":
		targets = [activator] if is_instance_valid(activator) else []
	else:
		targets.assign(source.get_tree().get_nodes_in_group("source_target_" + parts[0]))
	for target: Node in targets:
		if target.has_method("source_input"):
			target.set_meta("source_activator", activator if is_instance_valid(activator) else null)
			target.call("source_input", parts[1], parts[2])

static func teleport(body: Node3D, destination: Node3D) -> void:
	var data: Dictionary = destination.get("source_data")
	var angles := str(data.get("angles", "0 0 0")).split(" ", false)
	var yaw := deg_to_rad(float(angles[1]) - 90)
	var pitch := deg_to_rad(-float(angles[0]))
	if body.has_method("SourceTeleport"):
		body.call_deferred("SourceTeleport", destination.global_position, yaw, pitch)
	elif body is CharacterBody3D:
		body.set_deferred("global_position", destination.global_position)
		body.set_deferred("velocity", Vector3.ZERO)
