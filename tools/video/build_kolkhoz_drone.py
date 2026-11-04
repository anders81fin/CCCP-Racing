#!/usr/bin/env python3
"""Encode the Kolkhoz drone film: the uncut Movie Maker take + black name card, captions and music.

The captured AVI stays untouched. Captions are burned in here (timed from the baked path's marks), so
they can be re-worded without recapturing. Output goes under media/kolkhoz-drone-v1/ (a new directory per
version; nothing earlier is overwritten).

  build_kolkhoz_drone.py SOURCE.avi MARKS.json [--out NAME] [--no-captions]
"""
import argparse
import json
import textwrap
from pathlib import Path
import subprocess

from PIL import Image, ImageDraw

from build_trailer import text, PAPER, GOLD

ROOT = Path(__file__).resolve().parents[2]
TEXTS = json.loads((Path(__file__).resolve().parent / 'drone_texts.json').read_text())
FILMS = {   # film name: (output directory, music, source of the texts)
    'kolkhoz': (ROOT / 'media/kolkhoz-drone-v1', ROOT / 'assets/music/radio/Kevät saapuu Moskovaan.mp3'),
    'siberia': (ROOT / 'media/siberia-drone-v1', ROOT / 'assets/music/radio/Moskowaltz.mp3'),
}
CARD_END = 4.5


def run(args, log):
    with log.open('w') as stream:
        subprocess.run(args, stdout=stream, stderr=subprocess.STDOUT, check=True)


def probe(path):
    out = subprocess.check_output(['ffprobe', '-v', 'error', '-show_format', '-of', 'json', str(path)])
    return float(json.loads(out)['format']['duration'])


def card_png(path, big, small):
    img = Image.new('RGBA', (480, 270), (0, 0, 0, 255))
    draw = ImageDraw.Draw(img)
    long_text = len(small) > 4      # a longer story starts higher and sets its lines closer (as the game's own card does)
    big0, small0, step = (36, 112, 15) if long_text else (62, 150, 16)
    for i, line in enumerate(big):
        text(draw, line, big0 + i * 30, scale=3)
    for i, line in enumerate(small):
        text(draw, line, small0 + i * step, scale=1, color=PAPER)
    img.resize((1920, 1080), Image.Resampling.NEAREST).save(path)


def caption_png(path, line):
    lines = textwrap.wrap(line, 38)[:2]     # two lines at scale 2 (38 characters each)
    img = Image.new('RGBA', (480, 270), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    step = 20
    y = 270 - 16 - step * len(lines)
    # a soft dark strip under the text for legibility over any footage
    draw.rectangle((0, y - 6, 479, y + step * len(lines) + 2), fill=(10, 6, 4, 120))
    for i, line in enumerate(lines):
        text(draw, line, y + i * step, scale=2, color=GOLD)
    img.resize((1920, 1080), Image.Resampling.NEAREST).save(path)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('source')
    ap.add_argument('marks')
    ap.add_argument('--film', default='kolkhoz', choices=sorted(FILMS))
    ap.add_argument('--out', default=None)
    ap.add_argument('--no-captions', action='store_true')
    args = ap.parse_args()
    out_dir, music = FILMS[args.film]
    texts = TEXTS[args.film]
    hold = texts.get('card_hold', CARD_END)          # how long the black card holds (the flight starts when it has gone)
    delay = texts.get('film_delay', 0.0)
    if args.out is None:
        args.out = f'CCCP-Racing-{args.film.title()}-Drone-1080p60'
    marks = json.loads(Path(args.marks).read_text())['marks']
    source = Path(args.source)
    duration = probe(source)
    OUT = out_dir
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'overlays').mkdir(exist_ok=True)
    card = OUT / 'overlays/card.png'
    card_png(card, texts['card_big'], texts['card_small'])
    caps = []
    if not args.no_captions:
        for k, (mark, offset, length, line) in enumerate(texts['captions']):
            start = marks[mark] + offset + delay
            png = OUT / f'overlays/caption-{k:02d}.png'
            caption_png(png, line)
            caps.append((png, start, start + length))
    end = duration - 1.5
    music_len = probe(music)
    twice = duration > music_len - 1
    inputs = ['-i', str(source), '-i', str(music)] + (['-i', str(music)] if twice else []) + ['-loop', '1', '-i', str(card)]
    card_idx = 3 if twice else 2
    parts = ['[0:v]scale=1920:1080:flags=neighbor:in_range=full:out_range=tv,setsar=1,format=yuv420p[v0]',
             f'[{card_idx}:v]format=rgba,fade=t=out:st={hold-0.8}:d=0.8:alpha=1[card]',
             '[v0][card]overlay[v1]']
    last = 'v1'
    for i, (png, a, b) in enumerate(caps):
        inputs += ['-loop', '1', '-i', str(png)]
        idx = card_idx + 1 + i
        parts.append(f'[{idx}:v]format=rgba,fade=t=in:st={a:.2f}:d=0.4:alpha=1,fade=t=out:st={b-0.4:.2f}:d=0.4:alpha=1[c{i}]')
        parts.append(f"[{last}][c{i}]overlay=enable='between(t,{a:.2f},{b:.2f})'[v{i+2}]")
        last = f'v{i+2}'
    parts.append(f'[{last}]fade=t=in:d=0.45,fade=t=out:st={end}:d=1.5[outv]')
    parts.append('[0:a]volume=1.1[sfx]')
    # the song again behind itself (a 3 s crossfade) when the film is longer than the song
    src = '[1:a][2:a]acrossfade=d=3,' if twice else '[1:a]'
    parts.append(f'{src}volume=0.24,afade=t=in:d=1.5,afade=t=out:st={duration-3}:d=3[music]')
    parts.append('[sfx][music]amix=inputs=2:duration=first:normalize=0,'
                 f'alimiter=limit=0.6:level=0,afade=t=out:st={end}:d=1.5[outa]')
    output = OUT / f'{args.out}.mp4'
    cmd = ['ffmpeg', '-hide_banner', '-nostdin', '-y'] + inputs + [
        '-filter_complex', ';'.join(parts), '-map', '[outv]', '-map', '[outa]', '-t', str(duration),
        '-c:v', 'libx264', '-preset', 'fast', '-crf', '18', '-threads', '6',
        '-pix_fmt', 'yuv420p', '-color_range', 'tv', '-colorspace', 'bt709',
        '-color_primaries', 'bt709', '-color_trc', 'bt709',
        '-c:a', 'aac', '-b:a', '192k', '-ar', '48000', '-movflags', '+faststart',
        '-metadata', f'title=CCCP Racing - {" ".join(texts["card_big"])} - Drone Tour',
        '-metadata', 'comment=One-take in-engine drone flight and lap. Music: Juha Koskikuru.',
        str(output)]
    run(cmd, OUT / f'{args.out}-encode.log')
    run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-y', '-i', str(output),
         '-vf', f'fps=16/{duration},scale=384:216,tile=4x4', '-frames:v', '1',
         str(OUT / f'{args.out}-contact.jpg')], OUT / f'{args.out}-contact.log')
    print(f'{output}: {duration:.2f} s, {output.stat().st_size/1e6:.1f} MB', flush=True)


if __name__ == '__main__':
    main()
