extends SceneTree

# A stable window and main scene for Godot Movie Maker captures.
func _initialize():
    root.mode = Window.MODE_WINDOWED
    root.size = Vector2i(1280, 720)
    change_scene_to_file("res://scenes/Main.tscn")
