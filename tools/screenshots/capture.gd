extends SceneTree

# Real rendered frames, with fixed simulation timing and isolated save data.
var frames := 0
var label := "capture"
var output := "media/marketing-stills-2026-10-15/candidates"
var shots := [120, 180, 240, 300, 360, 420, 480]
var eye := Vector3.ZERO
var aim := Vector3.ZERO
var camera: Camera3D
var car: Node3D

func _initialize():
    root.mode = Window.MODE_WINDOWED
    root.size = Vector2i(1280, 720)
    root.content_scale_size = Vector2i(1920, 1080)
    root.content_scale_mode = Window.CONTENT_SCALE_MODE_VIEWPORT
    root.content_scale_aspect = Window.CONTENT_SCALE_ASPECT_KEEP
    for arg in OS.get_cmdline_user_args():
        if arg.begins_with("--still-label="):
            label = arg.trim_prefix("--still-label=")
        if arg.begins_with("--still-output="):
            output = arg.trim_prefix("--still-output=")
        if arg.begins_with("--still-eye="):
            eye = vector(arg.trim_prefix("--still-eye="))
        if arg.begins_with("--still-aim="):
            aim = vector(arg.trim_prefix("--still-aim="))
        if arg.begins_with("--still-frames="):
            shots = []
            for value in arg.trim_prefix("--still-frames=").split(","):
                shots.append(int(value))
    change_scene_to_file("res://scenes/Main.tscn")
    RenderingServer.frame_pre_draw.connect(pose)
    RenderingServer.frame_post_draw.connect(capture)

func vector(value: String) -> Vector3:
    var parts := value.split(",")
    return Vector3(float(parts[0]), float(parts[1]), float(parts[2]))

func pose():
    if eye == Vector3.ZERO:
        return
    if not is_instance_valid(car):
        car = root.find_child("Car", true, false)
    if not is_instance_valid(camera):
        camera = find_camera(root)
    if is_instance_valid(car) and is_instance_valid(camera):
        camera.global_position = car.global_position + eye
        camera.look_at(car.global_position + aim)
        camera.fov = 55
        camera.far = 400
        camera.force_update_transform()

func find_camera(node):
    if node is Camera3D:
        return node
    for child in node.get_children():
        var found = find_camera(child)
        if found != null:
            return found
    return null

func capture():
    frames += 1
    if frames in shots:
        var path := "%s/%s-%04d.png" % [output, label, frames]
        root.get_texture().get_image().save_png(path)
        print("STILL_SAVED ", path)
        var active_camera = find_camera(root)
        if active_camera != null:
            print("STILL_CAMERA fov=", active_camera.fov, " near=", active_camera.near,
                " position=", active_camera.global_position, " photography_override=", eye != Vector3.ZERO)
    if frames >= shots[-1]:
        quit()
