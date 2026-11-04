# Integration regression: a medal lap must keep the clock running, with cards enabled.
# Run with an isolated player file (repeat with --norm-step=7 for silver):
# godot-mono --path . --headless --fixed-fps 60 --script tools/tests/medal_celebration.gd -- --track=ilyich --start --autodrive --norm-step=4 --lap-done-at=1:1 --no-record --data-dir=/tmp/cccp-medal-test/
extends SceneTree
var hud
var age := 0.0
var awarded_at := -1.0
var lap_at_award := 0.0
var sampled := false
func _initialize():
    change_scene_to_file("res://scenes/Main.tscn")
func find_hud(node):
    if node.get_script() != null and str(node.get_script().resource_path).ends_with("Hud.cs"): return node
    for child in node.get_children():
        var found = find_hud(child)
        if found != null: return found
    return null
func _process(dt):
    age += dt
    if hud == null:
        hud = find_hud(root)
        return false
    if awarded_at < 0 and int(hud.get("RunMedal")) > 0:
        awarded_at = age
    if awarded_at >= 0 and not sampled and age > awarded_at + 0.1:
        lap_at_award = float(hud.get("LapTime"))
        sampled = true
    if sampled and age > awarded_at + 2:
        if bool(hud.get("Frozen")) or float(hud.get("LapTime")) < lap_at_award + 1.5:
            push_error("Medal paused the race: frozen=" + str(hud.get("Frozen")) + " time=" + str(hud.get("LapTime")) + " start=" + str(lap_at_award))
            quit(1)
        else:
            print("PASS: medal ", hud.get("RunMedal"), " awarded; clock advanced ", float(hud.get("LapTime")) - lap_at_award, " seconds without pausing")
            hud.call("CelebrateMedal", 3, "")
            hud.call("CelebrateMedal", 1, "")
            assert(int(hud.get("RunMedal")) == 3, "Badge must not downgrade")
            hud.call("ResetMedalCelebration")
            assert(int(hud.get("RunMedal")) == 0, "New run must clear the badge")
            print("PASS: persistent badge upgrades and resets")
            quit()
    if age > 18:
        push_error("No medal awarded")
        quit(1)
    return false
