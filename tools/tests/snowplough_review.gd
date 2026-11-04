# Capture the production snowplough in Siberia lighting, without moving the actor.
extends SceneTree
var output := "media/reviews/snowplough-gap-v1"
func _initialize():
	root.size=Vector2i(1280,720)
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--review-shots="): output=arg.trim_prefix("--review-shots=")
	call_deferred("review")
func find_script(parent, suffix):
	for node in parent.find_children("*", "", true, false):
		if node.get_script()!=null and node.get_script().resource_path.ends_with(suffix): return node
	return null
func review():
	if DirAccess.dir_exists_absolute(ProjectSettings.globalize_path("res://"+output)):
		push_error("Review output already exists; preserve earlier frames");quit(1);return
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://"+output))
	change_scene_to_file("res://scenes/Main.tscn")
	await create_timer(2).timeout
	var race=find_script(current_scene,"/RaceScene.cs")
	if race==null: push_error("No race scene");quit(1);return
	race.set_process(false);race.set_physics_process(false)
	var hud=find_script(race,"/Hud.cs")
	if hud: hud.visible=false
	var daylight=find_script(race,"/DayNight.cs")
	if daylight: daylight.call("Update",0.0,true,0,0.0,true,0.0)
	var ploughs=find_script(race,"/SnowPloughs.cs")
	var model:Node3D=null
	if ploughs:
		for node in ploughs.get_children():
			if node is Node3D and str(node.name).begins_with("Plough"): model=node;break
	if model==null: push_error("No production plough");quit(1);return
	var camera=Camera3D.new();race.add_child(camera);camera.current=true;camera.fov=42
	var views={"front":Vector3(0,3,-18),"overhead":Vector3(0,19,-7),"front-quarter":Vector3(-6,5,-13)}
	for label in views:
		camera.global_position=model.global_transform*views[label]
		camera.look_at(model.global_transform*Vector3(0,1,-6))
		await create_timer(.2).timeout
		await RenderingServer.frame_post_draw
		root.get_texture().get_image().save_png("res://"+output+"/"+label+".png")
	print("PASS: production snowplough front, overhead and quarter views captured")
	quit()
