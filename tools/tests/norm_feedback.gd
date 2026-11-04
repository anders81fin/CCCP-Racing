# Real lap verdicts: a plain norm win cheers without a card, then a miss boos without confetti.
# godot-mono --path . --headless --fixed-fps 60 --script tools/tests/norm_feedback.gd -- --track=ilyich --start --autodrive --lap-done-at=1:60,4:70 --no-record --data-dir=/tmp/cccp-norm-feedback-test/
extends SceneTree
var hud
var age := 0.0
var won_at := -1.0
var win_clock := 0.0
var checked_win := false
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
    var cheer = hud.get_node("NormCheer")
    var boo = hud.get_node("NormBoo")
    if won_at < 0 and bool(hud.get("NormConfettiActive")):
        if not require(cheer.playing and not boo.playing, "Norm win must cheer once"):
            return false
        if not require(int(hud.get("RunMedal")) == 0, "Plain win must not award a medal"):
            return false
        won_at = age
    if won_at >= 0 and not sampled and age > won_at + 0.1:
        win_clock = float(hud.get("LapTime"))
        sampled = true
    if sampled and not checked_win and age > won_at + 0.8:
        if not require(not bool(hud.get("Frozen")), "Norm win must not open a stopping card"):
            return false
        if not require(float(hud.get("LapTime")) > win_clock + 0.3, "Clock must keep running after win"):
            return false
        checked_win = true
        print("PASS: norm win cheers and shows confetti while race continues")
    if checked_win and boo.playing:
        if not require(not cheer.playing, "Boo must not overlap cheer"):
            return false
        if not require(not bool(hud.get("NormConfettiActive")), "Miss must not show confetti"):
            return false
        hud.call("ResetMedalCelebration")
        if not require(not boo.playing and not cheer.playing, "New run must stop norm voices"):
            return false
        if not require(not bool(hud.get("NormConfettiActive")), "New run must clear norm confetti"):
            return false
        print("PASS: miss boos without confetti; restart clears effects and voices")
        quit()
    if age > 18:
        push_error("Norm feedback did not trigger")
        quit(1)
    return false
func require(condition, message):
    if not condition:
        push_error(message)
        quit(1)
    return condition
