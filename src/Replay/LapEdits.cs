using System;
using System.Collections.Generic;
using System.Text.Json;

namespace CCCPRacing.Replay;

// =====================================================================================================================================================================
// Phase 4B: the saved editorial choices of a KEPT lap (docs/player-replay-phase4b-design.md, section 9 binds).
//
// INTERFACE CONTRACT (all on LapArchive, see LapArchive.Edits.cs; the Tasks never throw and complete on the archive worker, never block the UI thread):
//   Task<LapEditsLoad>   LoadEdits(string id)                    Kind: Missing | Ok | Invalid | NewerVersion | Mismatch | Refused (not kept / unreadable pair: Reason says why, "keep it first")
//   Task<EditsSaveResult> SaveEdits(string id, LapEdits edits)   the archive fills in the binding (hashes, fingerprint, tape size, duration) and Version; the caller supplies only the choices
//   Task<EditsSaveResult> ResetEdits(string id)                  atomically saves a validated DEFAULT sidecar bound to the pair; the previous good file stays as edits.json.bak
//   LapEntryInfo list markers (read when List() scans, parse only, NO hashing): Pinned, HasTrim, HasCameraKeys, EditsState (None | Ok | Unreadable | Mismatch | NewerVersion).
//       EditsState.Ok from a scan means "parses, validates, cheap binding (fingerprint, tape size, duration) agrees"; the SHA-256 binding is checked by LoadEdits only
//       (it may then refine the marker to Mismatch). A sidecar that fails the cheap checks is Mismatch/Unreadable at once; Pinned/HasTrim/HasCameraKeys are then false.
//   EditsSaveResult(Ok, Message, Id, Edits): Id is the lap id the task was started on (a UI shows success only for that archive + lap); Edits is the saved (bound) sidecar on success.
//
// Files: <laps>/<id>/edits.json (+ .tmp while staging, .bak = the previous good file, .bad-<n> = an unusable file that a save displaced; .bad files are never deleted and
// the .bak is never restored automatically). ghost.bin, tape.bin and meta.json are never opened for writing by this code.
// Binding: SHA-256 of the FULL ghost.bin and tape.bin ("absent" when the tape file does not exist), plus the legacy GhostFingerprint, TapeBytes and LapSeconds (1 ms).
// The hashes are computed on the archive worker only, once per file identity (path + length + mtime).
// Load never rewrites the sidecar: no silent rebind, no repair.
// =====================================================================================================================================================================

public enum LapEditsLoadKind { Missing, Ok, Invalid, NewerVersion, Mismatch, Refused }

public enum LapEditsState { None, Ok, Unreadable, Mismatch, NewerVersion }

public sealed record LapEditsLoad(LapEditsLoadKind Kind, LapEdits Edits, string Reason)
{
    public static LapEditsLoad Missing() => new(LapEditsLoadKind.Missing, null, "");
    public static LapEditsLoad Ok(LapEdits e) => new(LapEditsLoadKind.Ok, e, "");
    public static LapEditsLoad Invalid(string why) => new(LapEditsLoadKind.Invalid, null, why);
    public static LapEditsLoad NewerVersion(string why) => new(LapEditsLoadKind.NewerVersion, null, why);
    public static LapEditsLoad Mismatch(string which) => new(LapEditsLoadKind.Mismatch, null, which);
    public static LapEditsLoad Refused(string why) => new(LapEditsLoadKind.Refused, null, why);
}

public readonly record struct EditsSaveResult(bool Ok, string Message, string Id, LapEdits Edits);

public sealed record LapCameraKey
{
    public float T { get; init; }
    public string Mode { get; init; } = "";
    public LapFreeCamera Free { get; init; }
}

public sealed record LapEditsPlayback
{
    public float Speed { get; init; } = 1f;
    public bool Loop { get; init; }
    public string Camera { get; init; } = LapEdits.Auto;
    public bool Overlay { get; init; } = true;
}

public sealed record LapEdits
{
    public const int CurrentVersion = 3;
    public const string TopDown = "TopDown", Bonnet = "Bonnet", Cinematic = "Cinematic", Auto = "Auto", Free = "Free";
    public const string Absent = "absent";
    public static readonly string[] CameraModes = { TopDown, Bonnet, Cinematic, Auto, Free };
    public static readonly string[] PresetCameraModes = { TopDown, Bonnet, Cinematic, Auto };
    /// <summary>The viewer's speed set (RaceScene.ReplayViewerUi Speeds).</summary>
    public static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 2f };
    public const int MaxCameraKeys = 256;
    public const long MaxFileBytes = 1 << 20;

    public int Version { get; init; } = CurrentVersion;
    // binding
    public string GhostSha256 { get; init; } = "";
    public string TapeSha256 { get; init; } = "";
    public ulong GhostFingerprint { get; init; }
    public long TapeBytes { get; init; }
    public float LapSeconds { get; init; }
    // choices
    public bool Pinned { get; init; }
    public bool TrimEnabled { get; init; } = true;
    public float? In { get; init; }
    public float? Out { get; init; }
    public List<LapCameraKey> CameraKeys { get; init; } = new();
    public LapEditsPlayback Playback { get; init; } = new();

    public bool HasTrim => In != null || Out != null;
    public bool HasCameraKeys => CameraKeys != null && CameraKeys.Count > 0;

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>The JSON text. The edits must be valid (<see cref="Validate"/>): a non-finite float throws here.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>The default choices (nothing pinned, no trim, no keys), bound to the given pair.</summary>
    public static LapEdits Default(LapMeta meta, string ghostSha, string tapeSha, long tapeBytes)
        => new() { GhostSha256 = ghostSha, TapeSha256 = tapeSha, GhostFingerprint = meta.GhostFingerprint, TapeBytes = tapeBytes, LapSeconds = meta.LapSeconds };

    /// <summary>
    /// Tolerant reader of the text (unknown members ignored, missing optional ones default); never throws. Ok: <paramref name="edits"/> is set (not yet validated: call <see cref="Validate"/>).
    /// NewerVersion: the Version member is above this build's. Invalid: not JSON, wrong shape or an unusable value (reason says which).
    /// </summary>
    public static LapEditsLoadKind TryParse(string json, out LapEdits edits, out string reason)
    {
        edits = null; reason = "";
        int fileVersion = 1; // missing Version belongs to the original sidecar format
        try
        {
            using (var doc = JsonDocument.Parse(json ?? "", new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object) { reason = "not a JSON object"; return LapEditsLoadKind.Invalid; }
                if (doc.RootElement.TryGetProperty("Version", out var v))
                {
                    if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int ver)) { reason = "Version is not an integer"; return LapEditsLoadKind.Invalid; }
                    fileVersion = ver;
                    if (ver > CurrentVersion) { reason = $"saved by a newer game (edits version {ver})"; return LapEditsLoadKind.NewerVersion; }
                }
            }
            edits = JsonSerializer.Deserialize<LapEdits>(json, Options);
            if (edits == null) { reason = "empty"; return LapEditsLoadKind.Invalid; }
            edits = edits with { Version = fileVersion, CameraKeys = edits.CameraKeys ?? new(), Playback = edits.Playback ?? new() };
            return LapEditsLoadKind.Ok;
        }
        catch (Exception ex) { edits = null; reason = ex.Message; return LapEditsLoadKind.Invalid; }
    }

    static bool Fin(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

    static bool Hex64(string s)
    {
        if (s == null || s.Length != 64) return false;
        foreach (char c in s) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
        return true;
    }

    /// <summary>The first thing wrong with the values, or null. <paramref name="duration"/> is the lap's duration (meta.LapSeconds).</summary>
    public string Validate(float duration)
    {
        if (!Fin(duration) || duration <= 0) return "the lap duration is not usable";
        if (Version < 1 || Version > CurrentVersion) return $"Version {Version} is not supported";
        if (!Hex64(GhostSha256)) return "GhostSha256 is missing or not a SHA-256";
        if (!(Hex64(TapeSha256) || TapeSha256 == Absent)) return "TapeSha256 is missing or not a SHA-256";
        if (TapeBytes < 0) return "TapeBytes is negative";
        if (!Fin(LapSeconds) || LapSeconds <= 0) return "LapSeconds is not a positive finite number";
        if (In != null && (!Fin(In.Value) || In.Value < 0 || In.Value > duration)) return $"In is outside 0..{duration:F3}";
        if (Out != null && (!Fin(Out.Value) || Out.Value < 0 || Out.Value > duration)) return $"Out is outside 0..{duration:F3}";
        if (In != null && Out != null && !(In.Value < Out.Value)) return "In is not before Out";
        var keys = CameraKeys ?? new();
        if (keys.Count > MaxCameraKeys) return $"more than {MaxCameraKeys} camera keys";
        float prev = float.NegativeInfinity;
        for (int i = 0; i < keys.Count; i++)
        {
            var k = keys[i];
            if (k == null) return $"camera key {i} is empty";
            if (!Fin(k.T) || k.T < 0 || k.T > duration) return $"camera key {i} time is outside 0..{duration:F3}";
            if (Array.IndexOf(CameraModes, k.Mode) < 0) return $"camera key {i} has an unknown mode";
            if (k.Mode == Free)
            {
                if (Version < 2 || k.Free == null) return $"camera key {i} is missing a supported free-camera pose";
                if (Version < 3 && k.Free.Motion != null) return $"camera key {i}: animated shot requires edits version 3";
                if (k.Free.Validate() is string bad) return $"camera key {i}: {bad}";
            }
            else if (k.Free != null) return $"camera key {i} has a free pose on a preset mode";
            if (k.T == prev) return $"camera key {i} repeats the time of the key before it";
            if (k.T < prev) return $"camera keys are not sorted (key {i})";
            prev = k.T;
        }
        var p = Playback ?? new();
        if (!Fin(p.Speed) || Array.IndexOf(Speeds, p.Speed) < 0) return "Playback.Speed is not one of 0.25, 0.5, 1, 2";
        if (Array.IndexOf(PresetCameraModes, p.Camera) < 0) return "Playback.Camera is an unknown mode";
        return null;
    }

    /// <summary>Cheap binding checks (no file hashing): what disagrees with the meta, or null. Also used by the list scan.</summary>
    public string CheckCheap(LapMeta meta, long tapeBytes)
    {
        if (GhostFingerprint != meta.GhostFingerprint) return "ghost fingerprint";
        if (TapeBytes != tapeBytes) return "tape size";
        if (Math.Abs(LapSeconds - meta.LapSeconds) > 0.001f) return "lap duration";
        return null;
    }

    /// <summary>The full binding: cheap checks, then the SHA-256 of both files. Null when the sidecar belongs to this pair.</summary>
    public string CheckBinding(LapMeta meta, string ghostSha, string tapeSha, long tapeBytes)
        => CheckCheap(meta, tapeBytes) ?? (GhostSha256 != ghostSha ? "ghost.bin content" : TapeSha256 != tapeSha ? "tape.bin content" : null);
}
