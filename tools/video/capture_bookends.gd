extends SceneTree

# Uncut record of the real startup and resignation UI, driven through ordinary input.
# --cover --trailer-sfx --no-record. Keep the complete movie; shorten only its edit.
var elapsed := 0.0
var step := 0
var times := [6.7, 14.0, 16.5, 17.0]
var keys := [KEY_ENTER, KEY_ESCAPE, KEY_Y, KEY_ENTER]

func _initialize():
    root.mode = Window.MODE_WINDOWED
    root.size = Vector2i(1280, 720)
    change_scene_to_file("res://scenes/Main.tscn")

func _process(delta):
    elapsed += delta
    if step < times.size() and elapsed >= times[step]:
        var event := InputEventKey.new()
        event.keycode = keys[step]
        event.pressed = true
        Input.parse_input_event(event)
        print("BOOKEND input=", keys[step], " time=", elapsed)
        step += 1
    if elapsed > 26.0:
        quit()
    return false
