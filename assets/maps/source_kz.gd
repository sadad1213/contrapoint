extends Node3D

var running := false
var elapsed := 0.0
var finished := false
var display: Label

func _ready() -> void:
	var hud := CanvasLayer.new()
	hud.name = "KzTimer"
	add_child(hud)
	display = Label.new()
	display.set_anchors_and_offsets_preset(Control.PRESET_CENTER_TOP)
	display.position = Vector2(-180, 24)
	display.size = Vector2(360, 36)
	display.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	display.mouse_filter = Control.MOUSE_FILTER_IGNORE
	display.add_theme_font_override("font", preload("res://ui/net_graph_font.tres"))
	display.add_theme_color_override("font_color", Color.WHITE)
	display.add_theme_color_override("font_shadow_color", Color.BLACK)
	display.add_theme_constant_override("shadow_offset_x", 2)
	display.add_theme_constant_override("shadow_offset_y", 2)
	hud.add_child(display)

func kz_button(target: String) -> void:
	if target == "climb_startbutton":
		elapsed = 0
		running = true
		finished = false
	elif target == "climb_endbutton" and running:
		running = false
		finished = true

func _process(delta: float) -> void:
	if running:
		elapsed += delta
	if running or finished:
		display.text = "%02d:%06.3f%s" % [int(elapsed) / 60, fmod(elapsed, 60), "  Finished!" if finished else ""]
