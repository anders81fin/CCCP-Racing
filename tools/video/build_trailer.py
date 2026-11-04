#!/usr/bin/env python3
"""Rebuild the 80-second trailer from clean Godot captures in media/trailer/source.

Requires ffmpeg and Pillow. Run from any directory; --draft makes a 720p review.
The capture logs retain the frame numbers of the original lesson demonstrations.
"""
import argparse
import concurrent.futures
import json
from pathlib import Path
import re
import subprocess

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
WORK = ROOT / 'media/trailer'
SOURCE = WORK / 'source'
FPS = 30

# Source time, duration, sound gain, optional crop: all action comes from the game.
SHOTS = [
    ('Opening clutch kick', 'kolkhoz-driving', 1.2, 4, 1.0, None),
    ('Winter handbrake hook', 'siberia-driving', 6.3, 2, 1.0, (960, 540, 160, 90)),
    ('Kolkhoz identity', 'kolkhoz-scenic', 14.5, 3, .9, None),
    ('Siberia identity', 'siberia-scenic', 11.5, 3, .9, None),
    ('Complete Scandinavian flick', 'kolkhoz-driving', 5.6, 5, .9, None),
    ('Ridge at speed', 'kolkhoz-driving', 33, 3, .9, None),
    ('Stream crossing', 'kolkhoz-driving', 38.5, 2, .9, None),
    ('Complete grain store handbrake turn', 'kolkhoz-driving', 12, 5, .9, None),
    ('Silo and workers', 'kolkhoz-scenic', 27.5, 2, 1.0, None),
    ('Tractor in the fields', 'kolkhoz-scenic', 85, 2, 1.0, None),
    ('Party dacha', 'kolkhoz-scenic', 40, 2, 1.0, None),
    ('Drunken hunter misses', 'kolkhoz-scenic', 49, 4, .9, None),
    ('The duck survives', 'kolkhoz-scenic', 59, 2, 1.0, None),
    ('Snowbank turn', 'siberia-driving', .3, 3, .9, (960, 540, 160, 90)),
    ('Plough above the radar', 'siberia-scenic', 50.5, 3, .9, None),
    ('Ice exit', 'siberia-driving', 20.2, 3, .9, (960, 540, 160, 90)),
    ('Continuous MiG takeoff', 'siberia-scenic', 53.3, 7, .9, None),
    ('Radar and police chase', 'kolkhoz-driving', 77.8, 5, 1.0, None),
    ('Camp searchlights', 'siberia-scenic', 121, 2, 1.0, None),
    ('Gulag warning shot', 'kolkhoz-driving', 99.3, 2, .9, (960, 540, 160, 90)),
    ('Final flick', 'kolkhoz-driving', 8.3, 2, 1.0, None),
    ('Night afterburner', 'siberia-scenic', 146, 2, .9, None),
    ('Finish through the barn', 'kolkhoz-driving', 70.8, 4, 1.0, None),
    ('End card over the dacha at dawn', 'kolkhoz-scenic', 165, 8, .4, None),
]


def run(args, log):
    with log.open('w') as f:
        result = subprocess.run(args, stdout=f, stderr=f)
    if result.returncode:
        raise RuntimeError(f'{log}: {log.read_text()[-3000:]}')


def prepare_clip(item):
    index, shot = item
    label, source, start, duration, gain, crop = shot
    output = WORK / 'clips' / f'{index:02d}.mkv'
    if output.exists():
        return output
    vf = 'setpts=PTS-STARTPTS,fps=30'
    if crop:
        vf += ',crop=' + ':'.join(map(str, crop))
    # Movie Maker writes full-range MJPEG; make delivery colour range explicit.
    vf += ',scale=1280:720:flags=neighbor:in_range=full:out_range=tv,setsar=1,format=yuv420p'
    af = f'asetpts=PTS-STARTPTS,volume={gain},afade=t=in:d=0.025,afade=t=out:st={duration-.025}:d=0.025'
    run(['ffmpeg', '-hide_banner', '-nostdin', '-y', '-ss', str(start), '-i', str(SOURCE / f'{source}.avi'),
         '-t', str(duration), '-vf', vf, '-af', af, '-c:v', 'libx264', '-preset', 'veryfast',
         '-crf', '16', '-pix_fmt', 'yuv420p', '-color_range', 'tv', '-threads', '2',
         '-c:a', 'pcm_s16le', '-ar', '48000', '-ac', '2', str(output)], WORK / f'clip-{index:02d}.log')
    print(f'Clip {index:02d}: {label}', flush=True)
    return output


GLYPHS = {
    char.replace("\\'", "'").replace("\\\\", "\\"): pixels
    for char, pixels in re.findall(r"\['((?:\\.|[^'\\]))'\] = \"([.# ]+)\"", (ROOT / 'src/UI/PixelFont.cs').read_text())
}
GOLD, PAPER, RED, SHADOW = (255, 204, 51, 255), (237, 230, 204, 255), (219, 31, 20, 238), (76, 0, 0, 255)


def text(draw, value, y, scale=2, color=GOLD, x=None):
    value = value.upper()
    width = (len(value) * 6 - 1) * scale
    x = (480 - width) // 2 if x is None else x
    if width > 464:
        raise ValueError(f'Text does not fit: {value}')
    for dx, dy, col in [(scale, scale, SHADOW), (0, 0, color)]:
        for i, char in enumerate(value):
            for row, pixels in enumerate(GLYPHS.get(char, GLYPHS['?']).split()):
                for column, pixel in enumerate(pixels):
                    if pixel == '#':
                        px, py = x + i * 6 * scale + column * scale + dx, y + row * scale + dy
                        draw.rectangle((px, py, px + scale - 1, py + scale - 1), fill=col)


def logo(draw, y=38):
    draw.rectangle((0, y - 2, 479, y + 53), fill=RED)
    draw.rectangle((0, y - 2, 479, y - 1), fill=GOLD)
    draw.rectangle((0, y + 52, 479, y + 53), fill=GOLD)
    text(draw, 'CCCP ЯACIИG', y + 9, 5)
    text(draw, '*', y + 20, 2, x=43)
    text(draw, '*', y + 20, 2, x=425)


def overlay(name, painter):
    im = Image.new('RGBA', (480, 270))
    painter(ImageDraw.Draw(im))
    path = WORK / f'{name}.png'
    im.resize((1920, 1080), Image.Resampling.NEAREST).save(path)
    return path


def caption(draw, lines):
    top = 202 if len(lines) == 1 else 186
    draw.rectangle((10, top - 6, 469, top + len(lines) * 21 + 3), fill=(12, 10, 9, 215))
    draw.rectangle((10, top - 7, 469, top - 6), fill=GOLD)
    for i, line in enumerate(lines):
        text(draw, line, top + i * 21, 2, GOLD if i == 0 else PAPER)


def end_card(draw):
    draw.rectangle((0, 0, 479, 269), fill=(0, 0, 0, 160))
    logo(draw, 45)
    text(draw, 'PLAY THE DEVELOPMENT BUILD', 130, 2)
    text(draw, 'ON ITCH.IO', 154, 2, PAPER)
    text(draw, 'ANDERS81FIN.ITCH.IO/CCCP-RACING', 199, 1, PAPER)
    text(draw, 'WORK IN PROGRESS', 232, 1)


def make_overlays():
    def wip(d):
        d.rectangle((6, 5, 107, 18), fill=(0, 0, 0, 175))
        text(d, 'WORK IN PROGRESS', 8, 1, PAPER, x=11)
    return [
        (overlay('wip', wip), 0, 72),
        (overlay('logo', logo), 6, 9),
        (overlay('identity', lambda d: caption(d, ['A SATIRICAL ARCADE RALLY', 'DEEP IN THE SOVIET UNION'])), 9, 12),
        (overlay('plan', lambda d: caption(d, ['THE PLAN DEMANDS SPEED.'])), 17, 20),
        (overlay('duck', lambda d: caption(d, ['THE DUCK HAS IMMUNITY.'])), 37, 39),
        (overlay('state', lambda d: caption(d, ['THE STATE HAS OTHER PLANS.'])), 55, 58),
        (overlay('rules', lambda d: caption(d, ['YOUR LAP. THEIR RULES.'])), 68, 71.5),
        (overlay('end-card', end_card), 72, 80),
    ]


def duck(start, end, depth):
    return f'(1-{depth}*max(0,min(1,min((t-{start})/0.2,({end}-t)/0.4))))'


def build(draft=False):
    (WORK / 'clips').mkdir(parents=True, exist_ok=True)
    assert sum(s[3] for s in SHOTS) == 80
    for source in {s[1] for s in SHOTS}:
        if not (SOURCE / f'{source}.avi').exists():
            raise FileNotFoundError(source)
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        clips = list(pool.map(prepare_clip, enumerate(SHOTS)))
    listing = WORK / 'concat.txt'
    listing.write_text(''.join(f"file '{p.relative_to(WORK)}'\n" for p in clips))
    assembly = WORK / 'assembly.mkv'
    run(['ffmpeg', '-hide_banner', '-nostdin', '-y', '-f', 'concat', '-safe', '0', '-i', str(listing),
         '-c', 'copy', str(assembly)], WORK / 'assembly.log')
    overlays = make_overlays()
    music = ROOT / 'assets/music/radio/Soita sitä haitaria, Dmitri!.mp3'
    args = ['ffmpeg', '-hide_banner', '-nostdin', '-y', '-i', str(assembly), '-i', str(music)]
    for path, _, _ in overlays:
        args += ['-i', str(path)]
    filters = ['[0:v]scale=1920:1080:flags=neighbor,setsar=1[v0]']
    for i, (_, start, end) in enumerate(overlays):
        filters.append(f"[v{i}][{i+2}:v]overlay=eof_action=repeat:enable='gte(t,{start})*lt(t,{end})'[v{i+1}]")
    video = f'v{len(overlays)}'
    filters.append(f'[{video}]fade=t=out:st=78.8:d=1.2' + (',scale=1280:720:flags=neighbor' if draft else '') + '[video]')
    gain = '0.42*' + '*'.join([duck(34.9, 36.3, .85), duck(48, 55, .6), duck(55, 60, .65), duck(62.8, 63.7, .85)])
    filters.append(f"[1:a]atrim=duration=74,asetpts=PTS-STARTPTS,afade=t=in:d=0.25,afade=t=out:st=72.4:d=1.6,adelay=6000:all=1,volume='{gain}':eval=frame[music]")
    filters.append('[0:a][music]amix=inputs=2:duration=first:normalize=0,alimiter=limit=0.93:level=0,loudnorm=I=-16:TP=-1.5:LRA=11,aresample=48000,alimiter=limit=0.70:level=0,afade=t=out:st=78.8:d=1.2[audio]')
    output = WORK / ('CCCP-Racing-Trailer-Draft.mp4' if draft else 'CCCP-Racing-Trailer-1080p.mp4')
    args += ['-filter_complex', ';'.join(filters), '-filter_complex_threads', '1', '-map', '[video]', '-map', '[audio]',
             '-t', '80', '-r', '30', '-c:v', 'libx264', '-preset', 'veryfast' if draft else 'slow', '-crf', '22' if draft else '19',
             '-pix_fmt', 'yuv420p', '-profile:v', 'high', '-level:v', '4.1', '-color_range', 'tv', '-threads', '2',
             '-c:a', 'aac', '-b:a', '192k', '-ar', '48000', '-ac', '2', '-movflags', '+faststart',
             '-metadata', 'title=CCCP Racing - Development Trailer', str(output)]
    print(f'Compositing {output.name}', flush=True)
    run(args, WORK / ('draft.log' if draft else 'master.log'))
    t = 0
    edits = []
    for label, source, start, duration, gain, crop in SHOTS:
        edits.append(dict(title=label, timeline_start=t, duration=duration, source=source, source_start=start, sound_gain=gain, crop=crop))
        t += duration
    (WORK / 'edit-decisions.json').write_text(json.dumps(edits, indent=2) + '\n')
    print(f'Ready: {output} ({output.stat().st_size / 1e6:.1f} MB)', flush=True)
    if not draft:
        shared = WORK / 'CCCP-Racing-Trailer-WhatsApp.mp4'
        run(['ffmpeg', '-hide_banner', '-nostdin', '-y', '-i', str(output), '-vf', 'scale=1280:720:flags=neighbor',
             '-c:v', 'libx264', '-preset', 'slow', '-crf', '24', '-maxrate', '1300k', '-bufsize', '2600k', '-threads', '2',
             '-pix_fmt', 'yuv420p', '-profile:v', 'main', '-level:v', '3.1', '-c:a', 'aac', '-b:a', '128k',
             '-ar', '48000', '-movflags', '+faststart', str(shared)], WORK / 'whatsapp.log')
        run(['ffmpeg', '-hide_banner', '-nostdin', '-y', '-ss', '7.5', '-i', str(output), '-frames:v', '1',
             '-q:v', '2', str(WORK / 'CCCP-Racing-Trailer-Poster.jpg')], WORK / 'poster.log')
        print(f'Ready: {shared} ({shared.stat().st_size / 1e6:.1f} MB)', flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--draft', action='store_true')
    build(parser.parse_args().draft)
