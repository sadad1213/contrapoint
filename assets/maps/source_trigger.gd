extends Area3D

const IO = preload("res://assets/maps/source_io.gd")
@export var source_data: Dictionary = {}
var fired := false
var cooldown := 0.0
var enabled := true
var touching_teleport: Dictionary = {}

func _ready() -> void:
	enabled = str(source_data.get("StartDisabled", "0")) != "1"
	body_entered.connect(on_entered)
	body_exited.connect(on_exited)

func on_entered(body: Node3D) -> void:
	if preload("res://assets/maps/source_network.gd").is_client(self):
		return
	if not body is CharacterBody3D or not source_data.classname.begins_with("trigger_") or not enabled:
		return
	if source_data.classname == "trigger_teleport" and not overlaps_current_position(body):
		return
	if source_data.classname == "trigger_teleport":
		touching_teleport[body.get_instance_id()] = true
	IO.emit(self, source_data, "OnStartTouch", body)
	activate(body)

func overlaps_current_position(body: Node3D) -> bool:
	# Area notifications may still describe the body's old position after a
	# spawn/teleport. Confirm against the shape at its current world transform.
	var collider := body.get_node_or_null("CollisionShape3D") as CollisionShape3D
	if collider == null or collider.disabled:
		return false
	var query := PhysicsShapeQueryParameters3D.new()
	query.shape = collider.shape
	query.transform = collider.global_transform
	query.collision_mask = collision_layer
	query.collide_with_areas = true
	query.collide_with_bodies = false
	for hit: Dictionary in get_world_3d().direct_space_state.intersect_shape(query, 64):
		if hit.rid == get_rid():
			return true
	return false

func activate(body: Node3D) -> void:
	if preload("res://assets/maps/source_network.gd").is_client(self):
		return
	if cooldown > 0 or (source_data.classname == "trigger_once" and fired):
		return
	if source_data.classname == "trigger_teleport":
		if not overlaps_current_position(body):
			return
		touching_teleport[body.get_instance_id()] = true
	fired = true
	cooldown = maxf(float(source_data.get("wait", 0.2)), 0.015)
	IO.emit(self, source_data, "OnTrigger", body)
	if source_data.classname == "trigger_teleport":
		var destinations := get_tree().get_nodes_in_group("source_target_" + str(source_data.get("target", "")))
		if not destinations.is_empty():
			IO.teleport(body, destinations[0])

func on_exited(body: Node3D) -> void:
	if preload("res://assets/maps/source_network.gd").is_client(self):
		return
	if source_data.classname == "trigger_teleport" and not touching_teleport.erase(body.get_instance_id()):
		return
	if body is CharacterBody3D and enabled:
		IO.emit(self, source_data, "OnEndTouch", body)

func source_input(command: String, _parameter := "") -> void:
	if preload("res://assets/maps/source_network.gd").is_client(self):
		return
	match command.to_lower():
		"enable": enabled = true
		"disable": enabled = false
		"toggle": enabled = not enabled
		"kill": queue_free()

func _physics_process(delta: float) -> void:
	cooldown = maxf(0, cooldown - delta)
	if enabled and cooldown == 0 and source_data.classname in ["trigger_multiple", "trigger_teleport"]:
		for body in get_overlapping_bodies():
			if body is CharacterBody3D:
				activate(body)
