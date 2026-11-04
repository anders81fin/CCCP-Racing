#!/usr/bin/env python3
"""Replay MP4 export spike (dev only): run the spike scene under Godot's PNG/WAV movie writer from a job dir with a native 480x270 override.cfg, CONSUME the PNGs
while they are written (pipe into ffmpeg image2pipe, delete each owned PNG after its pipe write), gate the accepted range by the child's START/END markers, trim the WAV
to exact samples and mux. Prints the staging high-water mark."""
import os, subprocess, sys, time, shutil, struct, glob, json

root = sys.argv[1]; out = sys.argv[2]; fps = 60; native = (480, 270)
start_override = os.environ.get("SPIKE_START", "100"); frames_n = int(os.environ.get("SPIKE_FRAMES", "120"))
shutil.rmtree(out, ignore_errors=True); os.makedirs(out + "/movie")
ovr = out + "/override.cfg"   # (dev: beside project.godot is what Godot reads; here the dir of the project copy is the project itself, so we write it there for the run)
slow = float(os.environ.get("CONSUMER_DELAY", "0"))
proj_ovr = os.path.join(root, "override.cfg")
open(proj_ovr, "w").write("[display]\n\nwindow/size/viewport_width=480\nwindow/size/viewport_height=270\nwindow/size/mode=0\n")
env = dict(os.environ, SPIKE_OUT=out)
child = subprocess.Popen(["godot-mono", "--path", root, "--windowed", "--disable-vsync", "--fixed-fps", str(fps), "--write-movie", out + "/movie/m.png",
                          "--script", "res://tools/video/movie_spike.gd"], stdout=open(out + "/child.log", "w"), stderr=subprocess.STDOUT, env=env)
video = out + "/video.mp4"
ff = subprocess.Popen(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-f", "image2pipe", "-framerate", str(fps), "-c:v", "png", "-i", "pipe:0",
                       "-vf", "scale=1920:1080:flags=neighbor", "-c:v", "libx264", "-crf", "18", "-preset", "veryfast", "-pix_fmt", "yuv420p", video],
                      stdin=subprocess.PIPE, stderr=open(out + "/ffmpeg1.log", "w"))
def marker():
    try:
        d = {}
        for line in open(out + "/markers.txt"):
            p = line.split()
            if p: d[p[0]] = p[1:]
        return d
    except FileNotFoundError:
        return {}
nxt = 0; start = None; sent = 0; dropped = 0; hw = 0; first_sent = None
def path(i): return f"{out}/movie/m{i:08d}.png"
while True:
    alive = child.poll() is None
    m = marker()
    if start is None and "START" in m: start = int(m["START"][0])
    ready = os.path.exists(path(nxt + 1)) or (not alive and os.path.exists(path(nxt)))
    n_out = len(glob.glob(out + "/movie/*.png")); hw = max(hw, n_out)
    if ready:
        if start is None or nxt < start:
            if start is None and not os.path.exists(path(nxt + 1)) and alive: time.sleep(0.002); continue
            dropped += 1
        elif nxt < start + frames_n:
            if slow: time.sleep(slow)
            ff.stdin.write(open(path(nxt), "rb").read()); sent += 1
            if first_sent is None: first_sent = nxt
        else:
            dropped += 1
        os.remove(path(nxt)); nxt += 1
        continue
    if not alive: break
    time.sleep(0.002)
ff.stdin.close(); ff.wait()
print(f"consumer: first included image {first_sent}, sent {sent}, dropped {dropped}, staging high-water {hw} PNGs, child exit {child.returncode}")
# audio: exact sample trim
wav = out + "/movie/m.wav"; d = open(wav, "rb").read(); i = d.index(b"data"); n = struct.unpack("<I", d[i + 4:i + 8])[0]; pcm = d[i + 8:i + 8 + n]
spf = 800 * 8  # bytes per frame: 800 stereo frames * 2 ch * 4 bytes
trim = pcm[start * spf:(start + frames_n) * spf]
open(out + "/audio.raw", "wb").write(trim)
final = out + "/final.mp4"
subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", video, "-f", "s32le", "-ar", "48000", "-ac", "2", "-i", out + "/audio.raw", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", final], check=True)
os.remove(proj_ovr)
print("wav samples total", n // 8, "trimmed", len(trim) // 8, "markers", marker())
