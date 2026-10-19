extends Node3D

@export var definitions: Dictionary = {}
@export var points: Array = []
@export var audio_root := ""
var targets := {}
var sources: Array = []
var active: Dictionary = {}
var voices: Array = []
var random_sounds: Array = []
var check_remaining := 0.0

func _ready() -> void:
	for point: Dictionary in points:
		if point.has("targetname") and point.has("origin"):
			targets[point.targetname] = source_point(point.origin)
		if point.classname == "env_soundscape" and point.get("startdisabled", "0") == "0":
			sources.append(point)

func source_point(text: String) -> Vector3:
	var parts := text.split(" ", false)
	return Vector3(float(parts[0]), float(parts[2]), -float(parts[1])) * 0.0254

func _process(delta: float) -> void:
	var camera := get_viewport().get_camera_3d()
	if camera == null:
		return
	check_remaining -= delta
	if check_remaining <= 0:
		check_remaining = 0.25
		select_soundscape(camera)
	for entry: Dictionary in random_sounds:
		entry.remaining -= delta
		if entry.remaining <= 0:
			var voice: Node = entry.voice
			voice.stream = load_wave(entry.waves.pick_random(), false)
			voice.pitch_scale = range_value(entry.data.get("pitch", "100")) / 100
			voice.play()
			entry.remaining = maxf(0.1, range_value(entry.data.get("time", "5,15")))

func select_soundscape(camera: Camera3D) -> void:
	var nearest: Dictionary = {}
	var distance := INF
	for point: Dictionary in sources:
		var position := source_point(point.origin)
		var range_limit := float(point.get("radius", -1)) * 0.0254
		var current := camera.global_position.distance_to(position)
		if current >= distance or (range_limit > 0 and current > range_limit):
			continue
		var ray := PhysicsRayQueryParameters3D.create(camera.global_position, position + Vector3.UP * 0.05, 1)
		var player := camera.get_parent().get_parent() as CollisionObject3D
		if player:
			ray.exclude = [player.get_rid()]
		if not get_world_3d().direct_space_state.intersect_ray(ray).is_empty():
			continue
		nearest = point
		distance = current
	if nearest.is_empty():
		return
	var changed: bool = active.get("soundscape", "") != nearest.soundscape
	active = nearest
	if changed:
		for voice: Node in voices:
			voice.queue_free()
		voices.clear()
		random_sounds.clear()
		add_definition(str(active.soundscape).to_lower())
	else:
		for voice: Node in voices:
			if voice is AudioStreamPlayer3D:
				voice.position = anchor(str(voice.get_meta("position_index", "random")))

func range_value(value: String) -> float:
	var parts := value.split(",")
	return randf_range(float(parts[0]), float(parts[1])) if parts.size() == 2 else float(parts[0])

func anchor(index: String) -> Vector3:
	if index == "random":
		return source_point(active.origin) + Vector3(randf_range(-8, 8), 2, randf_range(-8, 8))
	return targets.get(active.get("position" + index, ""), source_point(active.origin))

func load_wave(path: String, looping: bool) -> AudioStream:
	path = path.trim_prefix("*").trim_prefix("#").trim_prefix("@").replace("\\", "/").to_lower()
	var stream := (load(audio_root + path) as AudioStream).duplicate() as AudioStream
	if looping and stream is AudioStreamWAV:
		stream.loop_mode = AudioStreamWAV.LOOP_FORWARD
		stream.loop_begin = 0
		stream.loop_end = int(stream.get_length() * stream.mix_rate)
	elif looping and (stream is AudioStreamMP3 or stream is AudioStreamOggVorbis):
		stream.loop = true
	return stream

func add_definition(name: String, position_override := "", volume_scale := 1.0, depth := 0) -> void:
	if depth > 8 or not definitions.has(name):
		return
	for pair: Array in definitions[name]:
		if not pair[1] is Array:
			continue
		var settings := {}
		var waves: Array = []
		for item: Array in pair[1]:
			settings[item[0]] = item[1]
			if item[0] == "rndwave":
				for wave: Array in item[1]:
					waves.append(wave[1])
			elif item[0] == "wave":
				waves.append(item[1])
		if pair[0] == "playsoundscape":
			add_definition(str(settings.name).to_lower(), str(settings.get("positionoverride", position_override)), volume_scale * range_value(str(settings.get("volume", "1"))), depth+1)
			continue
		if waves.is_empty():
			continue
		var index: String = position_override if position_override != "" else str(settings.get("position", ""))
		var voice: Node = AudioStreamPlayer.new() if index == "" else AudioStreamPlayer3D.new()
		add_child(voice)
		if voice is AudioStreamPlayer3D:
			voice.position = anchor(index)
			voice.unit_size = 4.0
			voice.max_distance = 40.0
			voice.set_meta("position_index", index)
		voice.volume_db = linear_to_db(maxf(0.001, range_value(str(settings.get("volume", "1"))) * volume_scale))
		voice.pitch_scale = range_value(str(settings.get("pitch", "100"))) / 100
		voices.append(voice)
		if pair[0] == "playlooping":
			voice.stream = load_wave(waves[0], true)
			voice.play()
		else:
			random_sounds.append({"voice": voice, "data": settings, "waves": waves, "remaining": range_value(str(settings.get("time", "5,15")))})
