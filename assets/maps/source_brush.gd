extends AnimatableBody3D

const IO = preload("res://assets/maps/source_io.gd")
@export var source_data: Dictionary = {}
@export var map_audio_root := ""
var closed_transform: Transform3D
var progress := 0.0
var opened := false
var close_remaining := -1.0
var health := 1.0
var broken := false
var button_remaining := 0.0
var train_target: Node3D
var train_speed := 0.0

func _ready() -> void:
	closed_transform = transform
	health = float(source_data.get("health", 1))
	if source_data.classname == "func_rotating":
		set_meta("no_impact_marks", true)
	if source_data.classname == "func_door" and int(source_data.get("spawnflags", 0)) & 1024:
		var bounds := AABB()
		var first := true
		for child in get_children():
			if child is CollisionShape3D and child.shape is ConvexPolygonShape3D:
				for point: Vector3 in child.shape.points:
					bounds = AABB(point, Vector3.ZERO) if first else bounds.expand(point)
					first = false
		if not first:
			var touch := Area3D.new()
			touch.collision_layer = 0
			touch.collision_mask = 1
			var box := BoxShape3D.new()
			box.size = bounds.size + Vector3.ONE * 0.08
			var shape := CollisionShape3D.new()
			shape.shape = box
			shape.position = bounds.get_center()
			touch.add_child(shape)
			add_child(touch)
			touch.body_entered.connect(func(body: Node3D):
				if body is CharacterBody3D: source_input("Open"))
	if source_data.classname == "func_tanktrain":
		call_deferred("configure_train")

func configure_train() -> void:
	var targets := get_tree().get_nodes_in_group("source_target_" + str(source_data.get("target", "")))
	if not targets.is_empty():
		train_target = targets[0]
		# The miniature sky room is expanded by the Source sky-camera scale.
		train_speed = float(source_data.get("startspeed", 0)) * 0.0254 * float(source_data.get("sky_scale", 1))

func source_use(_player: Node = null) -> void:
	if source_data.classname in ["func_door", "func_door_rotating"]:
		source_input("Close" if opened else "Open")
	elif source_data.classname == "func_button":
		set_meta("source_activator", _player)
		source_input("Press")

func source_input(command: String, parameter := "") -> void:
	if preload("res://assets/maps/source_network.gd").is_client(self):
		return
	match command.to_lower():
		"press":
			if source_data.classname != "func_button" or button_remaining > 0:
				return
			opened = true
			button_remaining = maxf(float(source_data.get("wait", 1)), 0.015)
			IO.emit(self, source_data, "OnPressed", get_meta("source_activator", null))
			var ancestor := get_parent()
			while ancestor != null:
				if ancestor.has_method("kz_button"):
					ancestor.call("kz_button", str(source_data.get("targetname", "")))
					break
				ancestor = ancestor.get_parent()
		"open":
			opened = true
			close_remaining = -1
		"close":
			opened = false
		"toggle":
			opened = not opened
		"kill":
			queue_free()
		"break":
			break_brush(global_position)
		"setanimation":
			for player: AnimationPlayer in find_children("*", "AnimationPlayer", true, false):
				for animation: String in player.get_animation_list():
					if animation.to_lower().ends_with(parameter.to_lower()):
						player.play(animation)
			if source_data.has("animation_sound"):
				play_sound(source_data.animation_sound)

func source_damage(amount: float, _point: Vector3, _direction: Vector3) -> void:
	if preload("res://assets/maps/source_network.gd").is_client(self):
		return
	if source_data.classname not in ["func_breakable_surf", "func_breakable"] or broken:
		return
	if source_data.classname == "func_breakable" and (int(source_data.get("spawnflags", 0)) & 1 or health <= 0):
		return
	if amount < float(source_data.get("minhealthdmg", 0)):
		return
	health -= amount
	if health <= 0:
		break_brush(_point)

func break_brush(point: Vector3) -> void:
	if broken or source_data.classname not in ["func_breakable_surf", "func_breakable"]:
		return
	broken = true
	var sound: String = source_data.get("break_sound", "physics/glass/glass_sheet_break%d.wav" % randi_range(1, 2))
	play_sound(sound, point)
	visible = false
	collision_layer = 0
	collision_mask = 0
	for child in get_children():
		if child is CollisionShape3D:
			child.set_deferred("disabled", true)
		elif child.has_method("break_brush"):
			child.call("break_brush", point)
	IO.emit(self, source_data, "OnBreak", get_meta("source_activator") if has_meta("source_activator") else null)

func play_sound(path: String, point := Vector3.INF) -> void:
	if map_audio_root == "":
		return
	var voice := AudioStreamPlayer3D.new()
	voice.stream = load(map_audio_root + path)
	get_parent().add_child(voice)
	voice.global_position = global_position if point == Vector3.INF else point
	var session := get_node_or_null("/root/LanSession")
	if session != null:
		session.call("MapSound", map_audio_root + path, voice.global_position)
	voice.unit_size = 3
	voice.finished.connect(voice.queue_free)
	voice.play()

func _physics_process(delta: float) -> void:
	var classname: String = source_data.classname
	var flags := int(source_data.get("spawnflags", 0))
	button_remaining = maxf(0, button_remaining - delta)
	if classname == "func_tanktrain" and is_instance_valid(train_target):
		global_position = global_position.move_toward(train_target.global_position, train_speed * delta)
		if global_position.distance_to(train_target.global_position) < 0.001:
			var point: Dictionary = train_target.get("source_data")
			var next := get_tree().get_nodes_in_group("source_target_" + str(point.get("target", "")))
			if not next.is_empty(): train_target = next[0]
	elif classname == "func_rotating" and flags & 1:
		var axis := Vector3.RIGHT if flags & 4 else Vector3.BACK if flags & 8 else Vector3.UP
		rotate(axis, deg_to_rad(float(source_data.get("maxspeed", 100))) * delta * (-1 if flags & 2 else 1))
	elif classname in ["func_door", "func_door_rotating", "func_button"]:
		if classname == "func_button" and button_remaining == 0:
			opened = false
		if close_remaining > 0:
			close_remaining -= delta
			if close_remaining <= 0:
				opened = false
		var distance := float(source_data.get("distance", 90))
		var speed := float(source_data.get("speed", 100))
		var direction := Vector3.ZERO
		var travel := 0.0
		if classname in ["func_door", "func_button"]:
			var angles := str(source_data.get("movedir", "0 0 0")).split(" ")
			var pitch := deg_to_rad(float(angles[0]))
			var yaw := deg_to_rad(float(angles[1]))
			direction = Vector3(cos(pitch)*cos(yaw), -sin(pitch), -cos(pitch)*sin(yaw))
			var bounds := AABB()
			var first := true
			for child in get_children():
				if child is CollisionShape3D and child.shape is ConvexPolygonShape3D:
					for point: Vector3 in child.shape.points:
						if first:
							bounds = AABB(point, Vector3.ZERO)
							first = false
						else:
							bounds = bounds.expand(point)
			travel = maxf(0, direction.abs().dot(bounds.size) - float(source_data.get("lip", 0)) * 0.0254)
			distance = travel / 0.0254
		var step := speed * delta / maxf(distance, 1)
		var old := progress
		progress = move_toward(progress, 1 if opened else 0, step)
		if classname == "func_door_rotating":
			var axis := Vector3.RIGHT if flags & 64 else Vector3.BACK if flags & 128 else Vector3.UP
			transform = closed_transform * Transform3D(Basis(axis, deg_to_rad(distance) * progress * (-1 if flags & 2 else 1)), Vector3.ZERO)
		else:
			position = closed_transform.origin + direction * travel * progress
		if progress == 1 and old < 1 and classname != "func_button":
			close_remaining = float(source_data.get("wait", 4))
