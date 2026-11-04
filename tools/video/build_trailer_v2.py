#!/usr/bin/env python3
"""Build the source-preserving second trailer from a JSON edit decision list.

EDL: {"shots": [{"title": "...", "source": "source/lap.avi",
"source_start": 0, "duration": 10, "gain": 1, "kind": "gameplay"}],
"end_card_duration": 4, "music_fade_start": 45, "music_fade_duration": 1}.
Duration is timeline seconds. Optional speed is allowed only for kind=ui.
Sources are never modified. Paths in the EDL are relative to its directory.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

from PIL import Image, ImageDraw
from build_trailer import end_card

ROOT = Path(__file__).resolve().parents[2]
DEFAULT = ROOT / 'media/trailer-v2/edit-decisions.json'
MUSIC = ROOT / 'assets/music/radio/Soita sitä haitaria, Dmitri!.mp3'


def probe(path):
    return json.loads(subprocess.check_output([
        'ffprobe', '-v', 'error', '-show_format', '-show_streams', '-of', 'json', str(path)]))


def run(args, log):
    with log.open('w') as stream:
        result = subprocess.run(args, stdout=stream, stderr=subprocess.STDOUT)
    if result.returncode:
        raise RuntimeError(f'{log}: {log.read_text()[-2500:]}')


def load_edl(path):
    data = json.loads(path.read_text())
    if isinstance(data, list):
        data = {'shots': data}
    shots = data.get('shots', [])
    if not shots:
        raise ValueError('EDL must contain shots')
    timeline = 0.0
    for original in shots:
        original.setdefault('source_start', 0)
        original.setdefault('gain', 1)
        original.setdefault('speed', 1)
        original.setdefault('kind', 'gameplay')
        if original['duration'] <= 0 or original['source_start'] < 0 or original['speed'] <= 0 or original['gain'] < 0:
            raise ValueError(f'Invalid shot values: {original}')
        if original['speed'] != 1 and original['kind'] != 'ui':
            raise ValueError('Only UI shots may change playback speed; gameplay stays real time')
        source = (path.parent / original['source']).resolve()
        if not source.is_file():
            raise FileNotFoundError(source)
        metadata = probe(source)
        if not any(s['codec_type'] == 'video' for s in metadata['streams']):
            raise ValueError(f'No video in {source}')
        needed = original['source_start'] + original['duration'] * original['speed']
        if needed > float(metadata['format']['duration']) + 0.05:
            raise ValueError(f'Shot extends past source end: {source}: {needed}')
        original['_path'] = str(source)
        original['_audio'] = any(s['codec_type'] == 'audio' for s in metadata['streams'])
        original['timeline_start'] = round(timeline, 6)
        timeline += original['duration']
    data.setdefault('end_card_duration', 4)
    if data['end_card_duration'] < 3:
        raise ValueError('End card needs at least three seconds')
    data.setdefault('music_gain', .23)
    data.setdefault('gameplay_gain', 1)
    data.setdefault('music_fade_start', max(0, timeline - 10))
    data.setdefault('music_fade_duration', 1)
    data.setdefault('fps', 30)
    if data.get('end_card_image') and not (path.parent / data['end_card_image']).is_file():
        raise FileNotFoundError(path.parent / data['end_card_image'])
    if data.get('opening_overlay_image'):
        if not (path.parent / data['opening_overlay_image']).is_file():
            raise FileNotFoundError(path.parent / data['opening_overlay_image'])
        data.setdefault('opening_overlay_duration', 3)
        if data['opening_overlay_duration'] <= 0:
            raise ValueError('Opening overlay duration must be positive')
    if data['fps'] not in (30, 60):
        raise ValueError('fps must be 30 or 60')
    if data['music_gain'] < 0 or data['gameplay_gain'] < 0 or data['music_fade_start'] < 0 or data['music_fade_duration'] <= 0:
        raise ValueError('Invalid music gain or fade timing')
    data['_footage_duration'] = timeline
    data['_duration'] = timeline + data['end_card_duration']
    return data


def atempo(speed):
    values = []
    while speed > 2:
        values.append(2)
        speed /= 2
    while speed < .5:
        values.append(.5)
        speed /= .5
    values.append(speed)
    return ','.join(f'atempo={v}' for v in values)


def encode_shot(shot, work, fps):
    source = Path(shot['_path'])
    fingerprint = dict(shot, size=source.stat().st_size, mtime=source.stat().st_mtime_ns, fps=fps, version=1)
    key = hashlib.sha256(json.dumps(fingerprint, sort_keys=True).encode()).hexdigest()[:20]
    output = work / 'clips' / f'{key}.mkv'
    if output.exists():
        return output
    duration, speed = shot['duration'], shot['speed']
    args = ['ffmpeg', '-hide_banner', '-nostdin', '-y', '-threads', '2', '-ss', str(shot['source_start']), '-i', str(source)]
    if not shot['_audio']:
        args += ['-f', 'lavfi', '-i', 'anullsrc=r=48000:cl=stereo']
    vf = f'setpts=(PTS-STARTPTS)/{speed},fps={fps},scale=1280:720:flags=neighbor:out_range=tv,setsar=1,format=yuv420p'
    af = f'asetpts=PTS-STARTPTS,{atempo(speed)},volume={shot["gain"]},afade=t=in:d=0.015,afade=t=out:st={duration-.015}:d=0.015'
    args += ['-map', '0:v:0', '-map', '0:a:0' if shot['_audio'] else '1:a:0', '-t', str(duration),
             '-vf', vf, '-af', af, '-filter_threads', '1', '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '16',
             '-threads', '2', '-color_range', 'tv', '-c:a', 'pcm_s16le', '-ar', '48000', '-ac', '2', str(output)]
    run(args, work / f'clip-{key}.log')
    return output


def build(edl, data, viewing_copies=False):
    work = edl.parent
    (work / 'clips').mkdir(exist_ok=True)
    fps = data['fps']
    clips = []
    for shot in data['shots']:
        print(f'Preparing {shot.get("title", shot["source"])}', flush=True)
        clips.append(encode_shot(shot, work, fps))
    card_image = (work / data['end_card_image']).resolve() if data.get('end_card_image') else work / 'end-card.png'
    if not data.get('end_card_image'):
        card = Image.new('RGBA', (480, 270), (0, 0, 0, 255))
        end_card(ImageDraw.Draw(card))
        card.convert('RGB').resize((1280, 720), Image.Resampling.NEAREST).save(card_image)
    card_clip = work / 'clips/end-card.mkv'
    run(['ffmpeg', '-hide_banner', '-nostdin', '-y', '-loop', '1', '-i', str(card_image),
         '-f', 'lavfi', '-i', 'anullsrc=r=48000:cl=stereo', '-t', str(data['end_card_duration']),
         '-r', str(fps), '-vf', 'format=yuv420p,setsar=1', '-filter_threads', '1', '-c:v', 'libx264',
         '-preset', 'veryfast', '-crf', '16', '-threads', '2', '-color_range', 'tv', '-c:a', 'pcm_s16le', str(card_clip)], work / 'end-card.log')
    clips.append(card_clip)
    listing = work / 'concat.txt'
    listing.write_text(''.join("file '" + str(p.resolve()).replace("'", "'\\''") + "'\n" for p in clips))
    assembly = work / 'assembly.mkv'
    run(['ffmpeg', '-hide_banner', '-nostdin', '-y', '-f', 'concat', '-safe', '0', '-i', str(listing), '-c', 'copy', str(assembly)], work / 'assembly.log')
    output_stem = data.get('output_stem', 'CCCP-Racing-Trailer-V2')
    if Path(output_stem).name != output_stem:
        raise ValueError('output_stem must be a filename without a directory')
    output = work / f'{output_stem}-1080p.mp4'
    duration = data['_duration']
    music = (edl.parent / data['music']).resolve() if data.get('music') else MUSIC
    overlay_inputs = []
    video_filter = '[0:v]scale=1920:1080:flags=neighbor,setsar=1[v];'
    if data.get('opening_overlay_image'):
        overlay_duration = data['opening_overlay_duration']
        overlay_inputs = ['-loop', '1', '-framerate', str(fps), '-t', str(overlay_duration),
                          '-i', str((work / data['opening_overlay_image']).resolve())]
        fade = min(.25, overlay_duration)
        video_filter = (
            '[0:v]scale=1920:1080:flags=neighbor,setsar=1[base];'
            f'[2:v]format=rgba,fade=t=out:st={overlay_duration-fade}:d={fade}:alpha=1[title];'
            f'[base][title]overlay=0:0:enable=\'lt(t,{overlay_duration})\'[v];'
        )
    filters = (
        video_filter +
        f'[1:a]asetpts=PTS-STARTPTS,volume={data["music_gain"]},afade=t=in:d=0.15,'
        f'afade=t=out:st={data["music_fade_start"]}:d={data["music_fade_duration"]}[music];'
        f'[0:a]volume={data["gameplay_gain"]}[game];'
        '[game][music]amix=inputs=2:duration=first:normalize=0,'
        'alimiter=limit=0.89:level=0:latency=1,aresample=48000[a]'
    )
    run(['ffmpeg', '-hide_banner', '-nostdin', '-y', '-threads', '2', '-i', str(assembly), '-stream_loop', '-1', '-i', str(music), *overlay_inputs,
         '-filter_complex', filters, '-filter_complex_threads', '1', '-map', '[v]', '-map', '[a]', '-t', str(duration),
         '-c:v', 'libx264', '-preset', data.get('encoding_preset', 'fast'), '-crf', '19', '-threads', '2', '-pix_fmt', 'yuv420p',
         '-color_range', 'tv', '-c:a', 'aac', '-b:a', '192k', '-ar', '48000', '-movflags', '+faststart',
         '-metadata', 'title=' + data.get('title', 'CCCP Racing — Cinematic Trailer'), '-metadata', 'comment=In-engine game footage. Music: Juha Koskikuru.', str(output)], work / 'master.log')
    shared = work / f'{output_stem}-720p.mp4'
    run(['ffmpeg', '-hide_banner', '-nostdin', '-y', '-threads', '2', '-i', str(output), '-vf', 'scale=1280:720:flags=neighbor',
         '-filter_threads', '1', '-c:v', 'libx264', '-preset', data.get('encoding_preset', 'fast'), '-crf', '24', '-threads', '2',
         '-c:a', 'aac', '-b:a', '128k', '-movflags', '+faststart', str(shared)], work / 'share.log')
    run(['ffmpeg', '-hide_banner', '-nostdin', '-y', '-i', str(output), '-vf', f'fps=12/{duration},scale=384:216,tile=4x3',
         '-filter_threads', '1', '-frames:v', '1', '-threads', '2', str(work / 'contact-sheet.jpg')], work / 'contact.log')
    run(['ffmpeg', '-hide_banner', '-nostdin', '-y', '-ss', str(min(12, data['_footage_duration']/2)), '-i', str(output),
         '-frames:v', '1', '-threads', '2', str(work / 'poster.jpg')], work / 'poster.log')
    if viewing_copies:
        folder = work / 'full-takes'
        folder.mkdir(exist_ok=True)
        for name in sorted({s['_path'] for s in data['shots']}):
            source = Path(name)
            target = folder / (source.stem + '-' + hashlib.sha256(name.encode()).hexdigest()[:8] + '.mp4')
            if target.exists():
                continue
            run(['ffmpeg', '-hide_banner', '-nostdin', '-y', '-threads', '2', '-i', name, '-c:v', 'libx264',
                 '-preset', 'fast', '-crf', '19', '-threads', '2', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-b:a', '192k',
                 '-movflags', '+faststart', str(target)], folder / (target.stem + '.log'))
    report = {'duration': duration, 'sources': [{k:v for k,v in s.items() if not k.startswith('_')} for s in data['shots']], 'outputs': {}}
    for path in (output, shared):
        info = probe(path)
        if abs(float(info['format']['duration']) - duration) > .15:
            raise RuntimeError(f'Output duration mismatch: {path}')
        report['outputs'][path.name] = info
    (work / 'render-report.json').write_text(json.dumps(report, indent=2) + '\n')
    print(f'Ready: {output}\nSharing: {shared}', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--edl', type=Path, default=DEFAULT)
    parser.add_argument('--validate-only', action='store_true', help='Probe source bounds and validate timing without rendering')
    parser.add_argument('--viewing-copies', action='store_true', help='Encode complete uncut takes as separate review MP4s')
    args = parser.parse_args()
    edl = args.edl.resolve()
    data = load_edl(edl)
    print(f'{len(data["shots"])} shots + end card; {data["_duration"]:.3f}s total')
    if not args.validate_only:
        build(edl, data, args.viewing_copies)


if __name__ == '__main__':
    main()
