extends SceneTree
## Export a generated cover in C64 multicolor bitmap constraints:
## 160x200 logical pixels (double-width on hardware), global black background,
## and three additional palette colors per 4x8 logical-pixel character cell.
## godot-mono --headless --path . --script tools/art/convert_loading_cover_c64.gd -- INPUT OUTPUT

const PALETTE = [
    Color('#000000'), Color('#ffffff'), Color('#813338'), Color('#75cec8'),
    Color('#8e3c97'), Color('#56ac4d'), Color('#2e2c9b'), Color('#edf171'),
    Color('#8e5029'), Color('#553800'), Color('#c46c71'), Color('#4a4a4a'),
    Color('#7b7b7b'), Color('#a9ff9f'), Color('#706deb'), Color('#b2b2b2'),
]

func _initialize():
    var args = OS.get_cmdline_user_args()
    if args.size() != 2:
        push_error('Expected input and output PNG paths')
        quit(1)
        return
    var source = Image.load_from_file(args[0])
    if source == null or source.is_empty():
        push_error('Could not load input image')
        quit(1)
        return
    source.resize(160, 200, Image.INTERPOLATE_LANCZOS)
    var result = Image.create(160, 200, false, Image.FORMAT_RGB8)
    for cy in range(25):
        for cx in range(40):
            var errors: Array = []
            for y in range(8):
                for x in range(4):
                    var color = source.get_pixel(cx * 4 + x, cy * 8 + y)
                    var distances: Array[float] = []
                    for candidate in PALETTE:
                        var delta = Vector3(color.r - candidate.r, color.g - candidate.g, color.b - candidate.b)
                        distances.append(delta.x * delta.x * .30 + delta.y * delta.y * .59 + delta.z * delta.z * .11)
                    errors.append(distances)
            var selected: Array[int] = [0]
            # Greedily choose the three colors that best reduce this cell's error.
            for slot in range(3):
                var best_color = 0
                var best_error = INF
                for candidate in range(1, 16):
                    if candidate in selected:
                        continue
                    var total = 0.0
                    for distances in errors:
                        var smallest: float = distances[candidate]
                        for existing in selected:
                            smallest = minf(smallest, distances[existing])
                        total += smallest
                    if total < best_error:
                        best_error = total
                        best_color = candidate
                selected.append(best_color)
            for y in range(8):
                for x in range(4):
                    var distances = errors[y * 4 + x]
                    var nearest = selected[0]
                    for candidate in selected:
                        if distances[candidate] < distances[nearest]:
                            nearest = candidate
                    result.set_pixel(cx * 4 + x, cy * 8 + y, PALETTE[nearest])
    var error = result.save_png(args[1])
    if error != OK:
        push_error('Could not save C64 cover: ' + str(error))
    else:
        print('Saved 160x200 C64 cover: ' + args[1])
    quit(0 if error == OK else 1)
