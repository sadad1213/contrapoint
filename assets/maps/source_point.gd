extends Node3D

const IO = preload("res://assets/maps/source_io.gd")
@export var source_data: Dictionary = {}
@export var map_audio_root := ""
var enabled := true
var remaining := 0.0
var voice: AudioStreamPlayer3D

func _ready() -> void:
	enabled = str(source_data.get("StartDisabled", "0")) != "1"
	reset_timer()
	if source_data.classname == "logic_auto":
		call_deferred("map_spawn")
	if source_data.classname == "ambient_generic":
		voice = AudioStreamPlayer3D.new()
		var path := map_audio_root + str(source_data.get("message", ""))
		if ResourceLoader.exists(path):
			voice.stream = load(path)
		voice.volume_db = linear_to_db(maxf(float(source_data.get("health", 10)) / 10.0, 0.001))
		voice.max_distance = float(source_data.get("radius", 1250)) * 0.0254
		voice.unit_size = 3
		add_child(voice)
		if not int(source_data.get("spawnflags", 0)) & 16 and voice.stream:
			voice.finished.connect(voice.play)
			voice.play()

func map_spawn() -> void:
	IO.emit(self, source_data, "OnMapSpawn")

func reset_timer() -> void:
	remaining = randf_range(float(source_data.get("LowerRandomBound", 1)), float(source_data.get("UpperRandomBound", 1))) if str(source_data.get("UseRandomTime", "0")) == "1" else maxf(float(source_data.get("RefireTime", 1)), 0.015)

func _physics_process(delta: float) -> void:
	if source_data.classname != "logic_timer" or not enabled:
		return
	remaining -= delta
	if remaining <= 0:
		IO.emit(self, source_data, "OnTimer")
		reset_timer()

func source_input(command: String, _parameter := "") -> void:
	if preload("res://assets/maps/source_network.gd").is_client(self):
		return
	match command.to_lower():
		"enable": enabled = true
		"disable": enabled = false
		"toggle": enabled = not enabled
		"kill": queue_free()
		"playsound":
			if voice and voice.stream: voice.play()
		"stopsound":
			if voice: voice.stop()
		"teleport":
			var activator = get_meta("source_activator", null)
			if is_instance_valid(activator) and activator is Node3D:
				IO.teleport(activator, self)
