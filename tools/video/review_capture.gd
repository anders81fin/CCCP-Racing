extends SceneTree

# Only low-frame-rate editorial previews use this entry point. The saved car
# is sampled at 30 Hz and world tape at 15 Hz; final photography retains the
# normal 120 Hz physics through capture.gd. No live driving uses this script.
func _initialize():
    Engine.physics_ticks_per_second = 30
    root.mode = Window.MODE_WINDOWED
    root.size = Vector2i(1280, 720)
    change_scene_to_file("res://scenes/Main.tscn")
