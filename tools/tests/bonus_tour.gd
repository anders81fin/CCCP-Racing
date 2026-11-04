# Normal menu integration: T TOUR, its waiting card, a complete lap, return and cancellation.
# godot-mono --path . --headless --fixed-fps 60 --script res://tools/tests/bonus_tour.gd -- --track=bonus --menu-single --no-intro --no-record --data-dir=/tmp/cccp-bonus-tour-test
# Optional graphical review: --review-shots=media/bonus-drone-v1/review-2
extends SceneTree
var failed := false
var elapsed := 0.0
var capturing := false
var index := 0
var out_dir := ""
var times := [0.5, 6.0, 12.0, 18.0, 24.0, 30.0, 38.0, 46.0, 55.0, 63.0, 73.0, 83.0, 90.0, 94.0, 100.0, 110.0, 120.0, 125.0, 140.0, 155.0, 165.0]
func _initialize():
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--review-shots="): out_dir = arg.trim_prefix("--review-shots=")
	if out_dir != "": DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://" + out_dir))
	call_deferred("run")
func check(ok, message):
	print(("PASS: " if ok else "FAIL: ") + message)
	failed = failed or not ok
func key(code):
	var event = InputEventKey.new()
	event.keycode = code
	event.physical_keycode = code
	event.pressed = true
	Input.parse_input_event(event)
	await create_timer(.12).timeout
	event = InputEventKey.new()
	event.keycode = code
	event.physical_keycode = code
	Input.parse_input_event(event)
	await create_timer(.12).timeout
func find_script(parent, suffix):
	for node in parent.find_children("*", "", true, false):
		if node.get_script() != null and node.get_script().resource_path.ends_with(suffix): return node
	return null
func _process(dt):
	if capturing:
		elapsed += dt
		if out_dir != "" and index < times.size() and elapsed >= times[index]:
			var stamp = times[index]
			index += 1
			snapshot.call_deferred("frame-%06.1f" % stamp)
	return false
func snapshot(label):
	if out_dir == "": return
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png("res://" + out_dir + "/" + label + ".png")
func run():
	change_scene_to_file("res://scenes/Main.tscn")
	await create_timer(1).timeout
	var race = find_script(current_scene, "/RaceScene.cs")
	var hud = find_script(race, "/Hud.cs")
	await key(KEY_ENTER)
	for i in range(3): await key(KEY_RIGHT)
	check(hud.get("CardSelection") == 3 and hud.get("CardTour"), "POLIGON 17 offers T TOUR on CHOOSE TRACK")
	await snapshot("menu")
	await key(KEY_T)
	await create_timer(1).timeout
	check(race.get("TrackPreviewActive") and race.get("DroneCardWaiting"), "T opens the waiting introduction card")
	await snapshot("card")
	await key(KEY_ENTER)
	check(race.get("TrackPreviewActive") and not race.get("DroneCardWaiting"), "Enter releases the card into the flight")
	capturing = true
	while race.get("TrackPreviewActive") and elapsed < 230:
		await process_frame
	capturing = false
	check(elapsed > 140 and elapsed < 210, "flight and driving lap finish within the expected tour duration")
	await create_timer(.3).timeout
	check(not race.get("TrackPreviewActive") and hud.get("CardSelection") == 3, "completed tour returns to the same track card")
	for suffix in ["/MechanicMarker.cs", "/ServiceMarker.cs"]:
		var marker = find_script(race, suffix)
		if marker: check(marker.visible, "service marker restored after tour: " + suffix)
	await key(KEY_T)
	await create_timer(.4).timeout
	check(race.get("DroneCardWaiting"), "tour can be reopened")
	await key(KEY_ESCAPE)
	await create_timer(.3).timeout
	check(not race.get("TrackPreviewActive") and not race.get("DroneCardWaiting"), "Escape cancels the card and returns to track selection")
	quit(1 if failed else 0)
