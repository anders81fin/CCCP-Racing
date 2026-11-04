# Run: godot-mono --path . --headless --audio-driver Dummy --script res://tools/tests/pause_instructions.gd -- --start --track=siberia --no-intro --no-record --data-dir=/tmp/cccp-pause-test
# Exercises a cross-track demonstration, cancellation, completion and preserving the paused race.
extends SceneTree
var failed = false
func check(ok, message):
    print(('PASS: ' if ok else 'FAIL: ') + message)
    failed = failed or not ok
func _initialize():
    call_deferred('run')
func key(code):
    var e = InputEventKey.new()
    e.keycode = code
    e.physical_keycode = code
    e.pressed = true
    Input.parse_input_event(e)
    await create_timer(.12).timeout
    e = InputEventKey.new()
    e.keycode = code
    e.physical_keycode = code
    e.pressed = false
    Input.parse_input_event(e)
    await create_timer(.12).timeout
func find_script(parent, suffix):
    for n in parent.find_children('*', '', true, false):
        if n.get_script() != null and n.get_script().resource_path.ends_with(suffix): return n
    return null
func run():
    change_scene_to_file('res://scenes/Main.tscn')
    await create_timer(3).timeout
    var race = find_script(current_scene, '/RaceScene.cs')
    var hud = find_script(race, '/Hud.cs')
    check(race != null, 'race starts')
    await key(KEY_ESCAPE)
    check(hud.get('Mode') == 3, 'Escape opens PARTY MEETING')
    var lap_time = hud.get('LapTime')
    var lap = hud.get('Lap')
    var fuel = hud.get('FuelLitres')
    var health = hud.get('EngineHealth')
    var car = find_script(race, '/CarVisual.cs')
    check(car != null, "original car found for preservation check")
    var car_transform = car.global_transform if car else Transform3D.IDENTITY
    await key(KEY_DOWN)
    check(hud.get('PauseSelection') == 1, 'Down selects next option')
    await key(KEY_UP)
    check(hud.get('PauseSelection') == 0, 'Up selects previous option')
    for i in range(3): await key(KEY_DOWN)
    await key(KEY_ENTER)
    check(hud.get('Mode') == 14, 'Enter on Instructions opens lesson tree')
    await key(KEY_ESCAPE)
    check(hud.get('Mode') == 3, 'Escape from tree returns to PARTY MEETING')
    await key(KEY_ENTER)
    await key(KEY_RIGHT)
    await key(KEY_DOWN)
    await key(KEY_ENTER)
    await create_timer(2).timeout
    check(race.process_mode == Node.PROCESS_MODE_DISABLED, 'race preserved and suspended while watching')
    var player = null
    for n in current_scene.find_children('*', '', true, false):
        if n != race and n.get_script() != null and n.get_script().resource_path.ends_with('/RaceScene.cs'): player = n
    check(player != null, 'instruction runs in its own scene')
    await key(KEY_ESCAPE)
    await key(KEY_ENTER)
    await create_timer(.5).timeout
    check(race.process_mode != Node.PROCESS_MODE_DISABLED, 'original race restored after leaving video')
    check(hud.get('Mode') == 3, 'video returns directly to PARTY MEETING')
    await key(KEY_ENTER)
    await key(KEY_ENTER)
    await create_timer(1).timeout
    for i in range(80):
        if race.process_mode != Node.PROCESS_MODE_DISABLED: break
        await key(KEY_ENTER)
    check(race.process_mode != Node.PROCESS_MODE_DISABLED and hud.get('Mode') == 3, 'completed lesson returns to PARTY MEETING')
    check(hud.get('LapTime') == lap_time and hud.get('Lap') == lap, 'lap and timer preserved through both lessons')
    check(hud.get('FuelLitres') == fuel and hud.get('EngineHealth') == health, 'fuel and damage preserved through both lessons')
    check(car == null or car.global_transform == car_transform, 'original car stays in the same position')
    for i in range(3): await key(KEY_UP)
    await key(KEY_ENTER)
    check(hud.get('Mode') in [1, 2], 'Continue resumes original race')
    quit(1 if failed else 0)
