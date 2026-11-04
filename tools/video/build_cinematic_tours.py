#!/usr/bin/env python3
"""Encode the two --cinematic-tour Movie Maker captures as 1080p60 MP4s.

Keeps the complete instructions lap, adds a short pixel title, fades, and music
under the captured spatial engine/tyre sounds. No cuts to the driving footage.
"""
import argparse
import json
from pathlib import Path
import subprocess

from PIL import Image, ImageDraw
from build_trailer import text, PAPER

ROOT = Path(__file__).resolve().parents[2]
WORK = ROOT / 'media/cinematic-tours'
TRACKS = {
    'ilyich': ('PATH OF ILYICH', 'KOLKHOZ / CINEMATIC LAP', 'Soita sitä haitaria, Dmitri!.mp3'),
    'siberia': ('SIBERIA', 'ICE / RUNWAY / LABOUR CAMP', 'Moskowaltz.mp3'),
}


def probe(path):
    return json.loads(subprocess.check_output([
        'ffprobe', '-v', 'error', '-show_format', '-show_streams', '-of', 'json', str(path)]))


def run(args, log):
    with log.open('w') as stream:
        subprocess.run(args, stdout=stream, stderr=subprocess.STDOUT, check=True)


def build(track):
    title, subtitle, song = TRACKS[track]
    source = WORK / 'source' / f'{track}.avi'
    duration = float(probe(source)['format']['duration'])
    overlay = Image.new('RGBA', (480, 270))
    draw = ImageDraw.Draw(overlay)
    draw.rectangle((12, 17, 467, 62), fill=(25, 12, 8, 185))
    text(draw, title, 23, scale=3)
    text(draw, subtitle, 49, scale=1, color=PAPER)
    title_path = WORK / f'{track}-title.png'
    overlay.resize((1920, 1080), Image.Resampling.NEAREST).save(title_path)
    output = WORK / f'CCCP-Racing-{track.title()}-Cinematic-1080p60.mp4'
    end = duration - 1.0
    filters = (
        '[0:v]scale=1920:1080:flags=neighbor:in_range=full:out_range=tv,setsar=1,format=yuv420p[v];'
        "[v][2:v]overlay=enable='between(t,0.6,3.8)',"
        f'fade=t=in:d=0.45,fade=t=out:st={end}:d=1.0[outv];'
        '[0:a]volume=1.1[sfx];'
        f'[1:a]volume=0.24,afade=t=in:d=1.5,afade=t=out:st={duration-2}:d=2[music];'
        '[sfx][music]amix=inputs=2:duration=first:normalize=0,'
        f'alimiter=limit=0.95:level=0,afade=t=out:st={end}:d=1[outa]'
    )
    run(['ffmpeg', '-hide_banner', '-nostdin', '-y', '-i', str(source),
         '-stream_loop', '-1', '-i', str(ROOT / 'assets/music/radio' / song),
         '-loop', '1', '-i', str(title_path), '-filter_complex', filters,
         '-map', '[outv]', '-map', '[outa]', '-t', str(duration),
         '-c:v', 'libx264', '-preset', 'fast', '-crf', '18', '-threads', '2',
         '-pix_fmt', 'yuv420p', '-color_range', 'tv', '-colorspace', 'bt709',
         '-color_primaries', 'bt709', '-color_trc', 'bt709',
         '-c:a', 'aac', '-b:a', '192k', '-ar', '48000', '-movflags', '+faststart',
         '-metadata', f'title=CCCP Racing - {title} - Cinematic Lap',
         '-metadata', 'comment=Instructions autopilot lap; cinematic in-engine cameras. Music: Juha Koskikuru.',
         str(output)], WORK / f'{track}-encode.log')
    run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-y', '-i', str(output),
         '-vf', f'fps=12/{duration},scale=384:216,tile=4x3', '-frames:v', '1',
         str(WORK / f'{track}-contact.jpg')], WORK / f'{track}-contact.log')
    print(f'{output}: {duration:.2f} seconds, {output.stat().st_size/1e6:.1f} MB', flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('tracks', nargs='*', choices=list(TRACKS))
    args = parser.parse_args()
    for track in args.tracks or TRACKS:
        build(track)
