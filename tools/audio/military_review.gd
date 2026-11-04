# Controlled audition of the real Hind synthesizers: spool-up, flyby and one 120-round burst.
# godot-mono --path . --headless --audio-driver PulseAudio --script res://tools/audio/military_review.gd
extends SceneTree
var clock := 0.0
var rotor:AudioStreamPlayer3D
var gun:AudioStreamPlayer3D
var recorder:AudioEffectRecord
var output := "media/military-highlight-v1/audio/hind-pass-v2.wav"
func _initialize():
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--audio-out="): output=arg.trim_prefix("--audio-out=")
	if FileAccess.file_exists(output):
		push_error("Choose a new audio output; existing auditions are preserved");quit(1);return
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://"+output.get_base_dir()))
	root.audio_listener_enable_3d=true
	var world=Node3D.new();root.add_child(world);current_scene=world
	var camera=Camera3D.new();world.add_child(camera);camera.make_current()
	var listener=AudioListener3D.new();world.add_child(listener);listener.make_current()
	var limiter=AudioEffectHardLimiter.new();limiter.ceiling_db=-1;limiter.pre_gain_db=0;limiter.release=.2
	AudioServer.add_bus_effect(0,limiter)
	recorder=AudioEffectRecord.new();AudioServer.add_bus_effect(0,recorder);recorder.set_recording_active(true)
	rotor=load("res://src/Audio/RotorSound.cs").new();rotor.set("Level",0.0);world.add_child(rotor)
	gun=load("res://src/Audio/HindGunSound.cs").new();world.add_child(gun)
func _process(dt):
	clock+=dt
	if rotor==null: return false
	rotor.set("Level",.9*clampf(clock/2.5,0,1) if clock<10 else 0.0)
	rotor.position=Vector3(-100+(clock-2)*25,14,25);gun.position=rotor.position
	gun.set("Firing",clock>=4 and clock<8.8)
	if clock>=12:
		recorder.set_recording_active(false)
		var sample=recorder.get_recording()
		var audible=false
		for byte in sample.data:
			if byte!=0: audible=true;break
		if not audible: push_error("Audio review recorded silence");quit(1);return false
		var result=sample.save_to_wav("res://"+output)
		print("PASS: real Hind flyby audio saved: ",output," result=",result)
		quit(0 if result==OK else 1)
	return false
