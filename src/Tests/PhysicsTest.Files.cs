using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>PERSONNEL FILES: the old progress becomes file 1 (the old files untouched), files are apart, erasing is a file's own.</summary>
    void PlayerFilesTest()
    {
        string root = "user://test_files/";
        string abs = ProjectSettings.GlobalizePath(root);
        foreach (string f in new[] { "campaign.cfg", "speedtrap.cfg", "settings.cfg", "leaderboard_ilyich.cfg" })
            if (FileAccess.FileExists(root + f)) DirAccess.RemoveAbsolute(root + f);
        for (int n = 1; n <= PlayerFiles.Count; n++) PlayerFiles.Erase(root, n);
        DirAccess.RemoveAbsolute(abs + "profiles/1"); DirAccess.RemoveAbsolute(abs + "profiles/2");
        DirAccess.RemoveAbsolute(abs + "profiles");
        DirAccess.MakeDirRecursiveAbsolute(abs);

        // The progress before files: medals, lessons, the name, and the shared settings beside them.
        var camp = new ConfigFile(); camp.SetValue("medals", "ilyich", 3); camp.Save(root + "campaign.cfg");
        var set = new ConfigFile();
        set.SetValue("radio", "volume", 7);
        set.SetValue("tutorial", "rally_seen", true);
        set.SetValue("driver", "name", "ANTTI");
        set.Save(root + "settings.cfg");
        var board = new ConfigFile(); board.SetValue("entry_0", "name", "ANTTI"); board.Save(root + "leaderboard_ilyich.cfg");
        string before = FileAccess.GetMd5(root + "campaign.cfg") + FileAccess.GetMd5(root + "settings.cfg") + FileAccess.GetMd5(root + "leaderboard_ilyich.cfg");

        PlayerFiles.Migrate(root);
        var one = PlayerFiles.Read(root, 1);
        Check(one.Used && one.Name == "ANTTI" && one.Medals["ilyich"] == NormPlan.Medal.Gold
              && new TutorialFile(PlayerFiles.Dir(root, 1)).RallySeen,
            "files: the progress so far becomes file 1 (medals, lessons, name)");
        var mine = new ConfigFile(); mine.Load(PlayerFiles.Dir(root, 1) + "settings.cfg");
        Check(!mine.HasSection("radio"), "files: the game's own settings stay shared, not in the file");
        string after = FileAccess.GetMd5(root + "campaign.cfg") + FileAccess.GetMd5(root + "settings.cfg") + FileAccess.GetMd5(root + "leaderboard_ilyich.cfg");
        Check(before == after && PlayerFiles.Active(root) == 1, "files: moving to files leaves the old files and the board as they were");

        PlayerFiles.Migrate(root);      // again: nothing more
        PlayerFiles.Activate(root, 2);
        var two = PlayerFiles.Read(root, 2);
        Check(PlayerFiles.Active(root) == 2 && !two.Used && two.Medals.Count == 0
              && !new TutorialFile(PlayerFiles.Dir(root, 2)).RallySeen && new Campaign(PlayerFiles.Dir(root, 2)).Best("ilyich") == NormPlan.Medal.None,
            "files: a new file starts clean (no medals, no lessons seen)");
        new Campaign(PlayerFiles.Dir(root, 2)).Award("siberia", NormPlan.Medal.Bronze);
        Check(new Campaign(PlayerFiles.Dir(root, 1)).Best("siberia") == NormPlan.Medal.None && PlayerFiles.Read(root, 2).Used,
            "files: a medal won in file 2 is file 2's alone");

        PlayerFiles.Erase(root, 1);
        Check(!PlayerFiles.Read(root, 1).Used && PlayerFiles.Read(root, 2).Used
              && FileAccess.GetMd5(root + "leaderboard_ilyich.cfg") + FileAccess.GetMd5(root + "campaign.cfg") != ""
              && FileAccess.FileExists(root + "leaderboard_ilyich.cfg") && FileAccess.FileExists(root + "campaign.cfg"),
            "files: erasing file 1 empties it alone, never the boards or the other files");

        // Tidy up the test's own directory (never the real records next to it).
        for (int n = 1; n <= PlayerFiles.Count; n++) { PlayerFiles.Erase(root, n); DirAccess.RemoveAbsolute(abs + $"profiles/{n}"); }
        DirAccess.RemoveAbsolute(abs + "profiles");
        foreach (string f in new[] { "campaign.cfg", "settings.cfg", "leaderboard_ilyich.cfg" }) DirAccess.RemoveAbsolute(abs + f);
        DirAccess.RemoveAbsolute(abs);
    }
}
