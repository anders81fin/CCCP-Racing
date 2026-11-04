extends SceneTree

# Trailer v3: uncut record of the real startup and resignation UI, driven through ordinary input.
# The startup goes one step further than v2: in the KGB archive a new personnel file is opened
# and the name ANATOLI is typed in, the main menu comes up for him; later Esc at the menu's top
# hands in the resignation (Y, Enter). --cover --archive --trailer-sfx --no-record.
var elapsed := 0.0
var step := 0
var release: InputEventKey = null     # each key is let go on the next frame, as a real key press is
# (time s, keycode, unicode)
var inputs := [
	[6.7, KEY_ENTER, 0],          # the test-version notice
	[12.6, KEY_ENTER, 0],         # open the first, new file: the name question
	[13.6, KEY_A, 65], [13.9, KEY_N, 78], [14.2, KEY_A, 65], [14.5, KEY_T, 84],
	[14.8, KEY_O, 79], [15.1, KEY_L, 76], [15.4, KEY_I, 73],
	[16.4, KEY_ENTER, 0],         # ANATOLI's file: the main menu
	[20.4, KEY_ESCAPE, 0],        # Esc at the top: the resignation
	[22.9, KEY_Y, 89],
	[23.4, KEY_ENTER, 0],
]

func _initialize():
	root.mode = Window.MODE_WINDOWED
	root.size = Vector2i(1280, 720)
	change_scene_to_file("res://scenes/Main.tscn")

func _process(delta):
	elapsed += delta
	if release != null:
		Input.parse_input_event(release)
		release = null
	elif step < inputs.size() and elapsed >= inputs[step][0]:
		var event := InputEventKey.new()
		event.keycode = inputs[step][1]
		event.physical_keycode = inputs[step][1]     # the game's actions match the physical key
		event.unicode = inputs[step][2]
		event.pressed = true
		Input.parse_input_event(event)
		release = event.duplicate()
		release.pressed = false
		print("BOOKEND input=", inputs[step][1], " time=", elapsed)
		step += 1
	if elapsed > 33.0:
		quit()
	return false
