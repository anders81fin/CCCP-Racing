extends SceneTree

# Trailer v3: an uncut record of a real rally from the normal driving camera (the game's own
# autopilot at the wheel), for close inserts. Same window as capture.gd; quits cleanly (so the
# movie is finalised) after --capture-for=SECONDS of game time.
var elapsed := 0.0
var limit := 120.0

func _initialize():
	root.mode = Window.MODE_WINDOWED
	root.size = Vector2i(1280, 720)
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--capture-for="):
			limit = float(a.substr("--capture-for=".length()))
	change_scene_to_file("res://scenes/Main.tscn")

func _process(delta):
	elapsed += delta
	if elapsed >= limit:
		quit()
	return false
