using System;
using System.IO;
using System.Threading.Tasks;

namespace CCCPRacing.Replay;

public readonly record struct LapDeleteResult(bool Ok, string Message);

public sealed partial class LapArchive
{
    /// <summary>Explicit player deletion, separate from automatic memory eviction. Removes only this
    /// archive's selected lap and its sidecars. Existing immutable playback/export sources stay valid.</summary>
    public Task<LapDeleteResult> Delete(string id)
    {
        if (_scanState == 0) List();
        Entry e;
        lock (_lock)
        {
            e = Find(id);
            if (e == null || e.Evicted) return Task.FromResult(new LapDeleteResult(false, "recording not found"));
            if (e.RetainBusy || e.EditsBusy || e.Deleting)
                return Task.FromResult(new LapDeleteResult(false, "recording is busy - wait and try again"));
            if (e.Dir == null)
            {
                _recent.Remove(e); ReleaseDeleted(e);
                Say("LAP ARCHIVE: deleted session recording " + id);
                return Task.FromResult(new LapDeleteResult(true, ""));
            }
            e.Deleting = true;
        }
        var done = new TaskCompletionSource<LapDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(() =>
        {
            LapDeleteResult result;
            try
            {
                WorkerHook?.Invoke("delete:" + id);
                string path = Path.GetFullPath(e.Dir);
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!string.Equals(Path.GetDirectoryName(path), Path.TrimEndingDirectorySeparator(_dir), comparison)
                    || Path.GetFileName(path) != id || new DirectoryInfo(path).LinkTarget != null)
                    throw new IOException("recording folder is outside this archive or is a link");
                _ops.Step("lap-delete");
                if (Directory.Exists(path)) Directory.Delete(path, true);
                else if (File.Exists(path)) throw new IOException("recording folder was replaced with a file");
                _hashes.TryRemove(Path.Combine(path, "ghost.bin"), out _);
                _hashes.TryRemove(Path.Combine(path, "tape.bin"), out _);
                lock (_lock) { _disk.Remove(id); _recent.Remove(e); ReleaseDeleted(e); }
                result = new LapDeleteResult(true, "");
                Say("LAP ARCHIVE: deleted kept recording " + id);
            }
            catch (Exception ex)
            {
                result = new LapDeleteResult(false, ex.Message);
                Say("LAP ARCHIVE: could not delete " + id + ": " + ex.Message);
            }
            lock (_lock) e.Deleting = false;
            done.TrySetResult(result);
        });
        return done.Task;
    }

    static void ReleaseDeleted(Entry e)
    {
        e.Evicted = true; e.Ghost = null; e.Tape = null; e.Rec = null; e.GhostSrc = null; e.Size = 0;
    }
}
