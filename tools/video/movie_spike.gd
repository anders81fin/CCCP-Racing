extends SceneTree
# Replay MP4 export feasibility spike: frame/audio alignment, markers and backpressure with Godot's built-in PNG/WAV movie writer.
# The picture is a flat colour whose red channel encodes (process frame % 256) so a decoded frame names its own index; at frame START a white-ish marker bar appears
# and a 10 ms square click starts in the same _process; START / END markers go to a file BEFORE the frame is rendered (so the consumer sees them before the PNG).
# Env: SPIKE_OUT=<dir> (marker file + the movie dir), SPIKE_START (default 100), SPIKE_FRAMES (default 120), SPIKE_MAXBACK (default 8: PNGs allowed outstanding), SPIKE_SLOWCONSUMER_TEST=1 logs waits.
var f := 0
var rect: ColorRect
var bar: ColorRect
var player: AudioStreamPlayer
var start := 100
var frames := 120
var maxback := 8
var out := ""
var waits := 0
var max_outstanding := 0
var marker: FileAccess

func _initialize():
	if OS.get_environment("SPIKE_SCALE") == "1":
		root.content_scale_mode = Window.CONTENT_SCALE_MODE_VIEWPORT
		root.content_scale_aspect = Window.CONTENT_SCALE_ASPECT_KEEP
		root.content_scale_size = Vector2i(480, 270)
	print("spike: window size ", root.size, " viewport rect ", root.get_visible_rect().size, " content scale ", root.content_scale_size, " mode ", root.content_scale_mode)
	out = OS.get_environment("SPIKE_OUT")
	if OS.get_environment("SPIKE_START") != "": start = int(OS.get_environment("SPIKE_START"))
	if OS.get_environment("SPIKE_FRAMES") != "": frames = int(OS.get_environment("SPIKE_FRAMES"))
	if OS.get_environment("SPIKE_MAXBACK") != "": maxback = int(OS.get_environment("SPIKE_MAXBACK"))
	rect = ColorRect.new(); rect.color = Color.BLACK; rect.size = Vector2(480, 270); root.add_child(rect)
	bar = ColorRect.new(); bar.color = Color(1, 1, 1); bar.position = Vector2(0, 0); bar.size = Vector2(480, 20); bar.visible = false; root.add_child(bar)
	var w := AudioStreamWAV.new()
	w.format = AudioStreamWAV.FORMAT_16_BITS; w.mix_rate = 48000; w.stereo = false
	var data := PackedByteArray()
	for i in range(480):
		var v := 20000 if (i / 24) % 2 == 0 else -20000
		data.append(v & 0xFF); data.append((v >> 8) & 0xFF)
	w.data = data
	player = AudioStreamPlayer.new(); player.stream = w; root.add_child(player)
	marker = FileAccess.open(out + "/markers.txt", FileAccess.WRITE)
	print("spike: after setup window ", root.size, " visible rect ", root.get_visible_rect().size)

func outstanding() -> int:
	var d := DirAccess.open(out + "/movie")
	if d == null: return 0
	var n := 0
	for fn in d.get_files():
		if fn.ends_with(".png"): n += 1
	return n

func _process(delta):
	var idx := Engine.get_process_frames()      # the image index this frame will become (spike: equal)
	# backpressure BEFORE advancing another movie frame: wait while too many PNGs are outstanding (bounded wait, then give up loudly)
	var t0 := Time.get_ticks_msec()
	while outstanding() > maxback:
		waits += 1
		OS.delay_msec(2)
		if Time.get_ticks_msec() - t0 > 20000:
			marker.store_line("ERROR consumer stalled at frame %d" % idx); marker.flush(); quit(2); return true
	max_outstanding = max(max_outstanding, outstanding())
	rect.color = Color(float(idx % 256) / 255.0, 0, 0)
	if idx == start:
		bar.visible = true
		player.play()
		marker.store_line("START %d" % idx); marker.flush()
	if idx == start + frames:
		marker.store_line("END %d" % idx); marker.store_line("WAITS %d MAXOUT %d" % [waits, max_outstanding]); marker.flush()
		quit()
	return false
