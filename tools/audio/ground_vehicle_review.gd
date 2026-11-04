# Actual engine synthesizer audition at 35 m: four seconds idle, then rising load.
# --audio-dir must name a new directory; no previous samples are overwritten.
extends SceneTree
var output := "media/ground-engine-v1/audio"
var profiles := [
	["tank",1,50.0,5.0,.025,3.0,380.0],
	["btr",2,38.0,4.0,.045,1.0,260.0],
	["kozlik",3,24.0,3.3,.10,1.0,180.0]]
var index := 0
var clock := 0.0
var engine:AudioStreamPlayer3D
var recorder:AudioEffectRecord
var world:Node3D
func _initialize():
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--audio-dir="): output=arg.trim_prefix("--audio-dir=")
	for profile in profiles:
		if FileAccess.file_exists(output+"/"+profile[0]+".wav"):
			push_error("Choose a new audio directory");quit(1);return
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://"+output))
	root.audio_listener_enable_3d=true
	world=Node3D.new();root.add_child(world);current_scene=world
	var camera=Camera3D.new();world.add_child(camera);camera.make_current()
	var listener=AudioListener3D.new();world.add_child(listener);listener.make_current()
	var limiter=AudioEffectHardLimiter.new();limiter.ceiling_db=-1;limiter.pre_gain_db=0;limiter.release=.2
	AudioServer.add_bus_effect(0,limiter)
	recorder=AudioEffectRecord.new();AudioServer.add_bus_effect(0,recorder)
	start_voice()
func start_voice():
	var profile=profiles[index]
	engine=load("res://src/Audio/DieselSound.cs").new()
	engine.set("Voice",profile[1]);engine.set("BaseRate",profile[2]);engine.set("RatePerSpeed",profile[3])
	engine.set("Body",profile[4]);engine.set("Loudness",profile[5]);engine.set("Reach",profile[6])
	engine.position=Vector3(0,1,35);world.add_child(engine)
	recorder.set_recording_active(true)
func _process(dt):
	if engine==null: return false
	clock+=dt
	engine.set("Speed",0.0 if clock<4 else minf(8,(clock-4)*2))
	if clock>=8:
		recorder.set_recording_active(false)
		var sample=recorder.get_recording()
		var audible=false
		for byte in sample.data:
			if byte!=0: audible=true;break
		if not audible: push_error("Engine recording is silent");quit(1);return false
		var path="res://"+output+"/"+profiles[index][0]+".wav"
		if sample.save_to_wav(path)!=OK: push_error("Cannot save engine review");quit(1);return false
		print("PASS: engine idle/load at 35 m: ",path)
		engine.free();engine=null;index+=1;clock=0
		if index>=profiles.size(): quit()
		else: start_voice()
	return false
