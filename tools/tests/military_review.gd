# Native model review in the actual bonus-track lighting, without changing gameplay cameras.
# godot-mono --path . --script res://tools/tests/military_review.gd -- --track=bonus --no-intro --no-record --data-dir=/tmp/cccp-military-review
extends SceneTree
var output := "media/military-highlight-v1/close-review-colour"
func _initialize():
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--review-shots="): output=arg.trim_prefix("--review-shots=")
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://"+output))
	call_deferred("run")
func find_script(parent, suffix):
	for node in parent.find_children("*", "", true, false):
		if node.get_script()!=null and node.get_script().resource_path.ends_with(suffix): return node
	return null
func snapshot(label):
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png("res://"+output+"/"+label+".png")
func run():
	change_scene_to_file("res://scenes/Main.tscn")
	await create_timer(1.5).timeout
	var race=find_script(current_scene,"/RaceScene.cs")
	if race==null: push_error("No race for military review");quit(1);return
	var hud=find_script(race,"/Hud.cs")
	if hud: hud.set("Clean",true);hud.visible=false
	await create_timer(.8).timeout
	var daylight=find_script(race,"/DayNight.cs")
	if daylight: daylight.call("Update",0.0,true,0,0.0,true,0.0)
	race.set_process(false);race.set_physics_process(false)
	var camera:Camera3D=null
	for node in race.find_children("*","Camera3D",true,false):
		if node.current: camera=node;break
	var hind:Node3D=null
	var tank:Node3D=null
	for node in race.find_children("*","Node3D",true,false):
		if hind==null and node.has_node("Airframe") and node.has_node("Rotor"): hind=node
		if tank==null and str(node.name).begins_with("T-72"): tank=node
	if camera==null or hind==null or tank==null:
		push_error("Missing native military models or camera");quit(1);return
	camera.fov=43
	for label in ["hind-front","hind-side","tank-front","tank-side"]:
		var model=hind if label.begins_with("hind") else tank
		var offset=Vector3(-8,6,-12) if label.ends_with("front") else Vector3(-12,7,3)
		camera.global_position=model.global_position+model.global_basis*offset
		camera.look_at(model.global_position+Vector3.UP*2)
		await create_timer(.25).timeout
		await snapshot(label)
	print("PASS: four native military model views captured in bonus-track lighting")
	quit()
