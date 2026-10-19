extends Node3D

@export var count := 10
@export var radius := 2.4
@export var model: PackedScene
var fish: Array[Node3D] = []
var phases: Array[float] = []
var age := 0.0

func _ready() -> void:
	for i in count:
		var instance := model.instantiate() as Node3D
		instance.name = "Goldfish_%02d" % i
		add_child(instance)
		fish.append(instance)
		phases.append(i * TAU / count + 0.21)

func _process(delta: float) -> void:
	age += delta
	for i in fish.size():
		var phase := phases[i] + age * (0.15 + i * 0.009)
		var range_factor := 0.4 + (i % 4) * 0.14
		fish[i].position = Vector3(cos(phase) * radius * range_factor, sin(age * 0.8 + phases[i]) * 0.05, sin(phase) * radius * range_factor)
		fish[i].rotation.y = -phase - PI * 0.5 + sin(age * 7 + phases[i]) * 0.07
