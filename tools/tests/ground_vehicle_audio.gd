# Real bonus-race audio wiring: active engines, real 3D listener and an unmuted Sfx path.
# -- --start --track=bonus --no-intro --no-record --data-dir=/tmp/cccp-ground-audio-game
extends SceneTree
var failed := false
func _initialize(): call_deferred("run")
func check(ok,message):
	print(("PASS: " if ok else "FAIL: ")+message)
	failed=failed or not ok
func run():
	change_scene_to_file("res://scenes/Main.tscn")
	await create_timer(.5).timeout
	Input.action_press("clutch")
	await create_timer(8).timeout
	var voices := [0,0,0,0]
	for node in current_scene.find_children("*","",true,false):
		if node.get_script()==null or not node.get_script().resource_path.ends_with("/DieselSound.cs"): continue
		var voice=int(node.get("Voice"))
		if voice==0: continue
		voices[voice]+=1
		var viewport=node.get_viewport()
		check(node.playing and not node.stream_paused and node.can_process(),"engine voice %d runs in the actual bonus race"%voice)
		check(viewport.audio_listener_enable_3d and viewport.get_audio_listener_3d()!=null,"engine voice %d has an active 3D listener"%voice)
		check(node.bus=="Diesel" and not AudioServer.is_bus_mute(AudioServer.get_bus_index("Diesel")),"engine voice %d reaches the machinery mix"%voice)
	check(voices[1]>=2 and voices[2]==2 and voices[3]==1,"actual race creates tank, BTR and bear-jeep engine voices")
	check(not AudioServer.is_bus_mute(AudioServer.get_bus_index("Sfx")) and not AudioServer.is_bus_mute(0),"race Sfx and Master are unmuted")
	Input.action_release("clutch")
	if "--audio-lifecycle-census" in OS.get_cmdline_user_args(): dump_playbacks(current_scene)
	current_scene.call("RequestQuit",1 if failed else 0)

# Map native leak IDs to production player owners without retaining playback references.
func dump_playbacks(node: Node):
	if node is AudioStreamPlayer or node is AudioStreamPlayer3D:
		if node.playing:
			var playback=node.get_stream_playback()
			if playback is AudioStreamGeneratorPlayback:
				var script=node.get_script()
				print("AUDIO OWNER id=",playback.get_instance_id()," path=",node.get_path()," script=",script.resource_path if script!=null else "native child")
	for child in node.get_children(): dump_playbacks(child)
