using System;
using System.Collections.Generic;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// DRIVING SCHOOL on the title: the lessons as a tree. CAR HANDLING (the driving school) and each open
/// track with a rally school, folded; Right unfolds one into its chapters, Left folds it again; Enter
/// on a course shows all of it, on a chapter only that one. The lesson runs on its own track (the
/// world rebuilt first if need be) and comes back to the tree where it was.
/// </summary>
public partial class RaceScene
{
    bool _showLessons;
    // Kept over a track change (the scene is rebuilt for a track's school).
    static readonly HashSet<string> _lessonsOpen = new();
    static int _lessonSel;
    static (SchoolCourse course, string track, int chapter)? _lessonResume;
    static bool _lessonsBack;

    readonly record struct LessonRow(string Key, string Text, bool Header, bool Open, SchoolCourse Course, string Track, int Chapter);

    /// <summary>The tree as it stands: each course's row, its chapters under it if unfolded.</summary>
    List<LessonRow> LessonRows()
    {
        var rows = new List<LessonRow>();
        void Course(string key, string name, SchoolCourse c, string track)
        {
            bool open = _lessonsOpen.Contains(key);
            rows.Add(new LessonRow(key, name, true, open, c, track, -1));
            if (!open) return;
            var chapters = SchoolScript.Chapters(c, track);
            for (int i = 0; i < chapters.Length; i++) rows.Add(new LessonRow(key, chapters[i], false, false, c, track, i));
        }
        Course("driving", "CAR HANDLING", SchoolCourse.Driving, SchoolScript.DrivingTrack);
        for (int i = 0; i < Tracks.Length; i++)
            if (SchoolScript.For(SchoolCourse.Rally, Tracks[i].id) != null && !Locked(i, MenuItem.Race, anyFuel: true))
                Course(Tracks[i].id, Tracks[i].name, SchoolCourse.Rally, Tracks[i].id);
        return rows;
    }

    void EnterLessons()
    {
        _showLessons = true;
        if (_state != State.Paused) _menu = MenuItem.School;
        _overlayFrame = Engine.GetProcessFrames();
        _hud.Mode = HudMode.Lessons;
        LessonsHud();
    }

    void CloseLessons()
    {
        _showLessons = false;
        _overlayExitFrame = Engine.GetProcessFrames();
        _hud.Mode = _state == State.Paused ? HudMode.Paused : HudMode.Title;
    }

    /// <summary>The tree's raw input (keys; a pad's d-pad, A and B come as keys): true while it is up.</summary>
    bool LessonsInput(InputEvent e)
    {
        if (!_showLessons) return false;
        if (Engine.GetProcessFrames() <= _overlayFrame || e is not InputEventKey { Pressed: true } k) return true;
        var rows = LessonRows();
        _lessonSel = Math.Clamp(_lessonSel, 0, rows.Count - 1);
        var row = rows[_lessonSel];
        switch (k.Keycode)
        {
            case Key.Up or Key.W: _lessonSel = Math.Max(0, _lessonSel - 1); break;
            case Key.Down or Key.S: _lessonSel = Math.Min(rows.Count - 1, _lessonSel + 1); break;
            case Key.Right or Key.D when row.Header && !k.Echo: _lessonsOpen.Add(row.Key); break;
            case Key.Left or Key.A when !k.Echo:
                if (row.Header) _lessonsOpen.Remove(row.Key);
                else
                {
                    // On a chapter: fold its course and stand on it.
                    _lessonsOpen.Remove(row.Key);
                    _lessonSel = LessonRows().FindIndex(r => r.Header && r.Key == row.Key);
                }
                break;
            case Key.Escape when !k.Echo: CloseLessons(); return true;
            case Key.Enter or Key.KpEnter when !k.Echo && !k.AltPressed:
                StartLesson(row.Course, row.Track, row.Chapter);
                return true;
        }
        LessonsHud();
        return true;
    }

    /// <summary>A course (chapter -1: all of it) or one chapter, on its own track.</summary>
    void StartLesson(SchoolCourse course, string track, int chapter)
    {
        if (_state == State.Paused)
        {
            _showLessons = false;
            _overlayExitFrame = Engine.GetProcessFrames();
            CallDeferred(nameof(OpenInstruction), (int)course, track, chapter);
            return;
        }
        _showLessons = false;
        _overlayExitFrame = Engine.GetProcessFrames();
        _lessonResume = (course, track, chapter);
        MenuPlayed(MenuItem.School);
        if (!OnTrack(track, MenuItem.School)) return;     // rebuilt on it: ResumeAfterReload starts it
        ResumeLesson();
    }

    /// <summary>The chosen lesson, on its track now; the tree comes back when it ends.</summary>
    void ResumeLesson()
    {
        if (_lessonResume is not var (course, _, chapter)) return;
        _lessonResume = null;
        _lessonsBack = true;
        EnterSchool(course, true, chapter);
    }

    /// <summary>A lesson from the tree has ended: back on the tree, where it was.</summary>
    bool LessonsBack()
    {
        if (!_lessonsBack) return false;
        _lessonsBack = false;
        EnterTitle();
        OpenMenu(MenuItem.Single, MenuItem.School);
        EnterLessons();
        return true;
    }

    void LessonsHud()
    {
        var rows = LessonRows();
        _lessonSel = Math.Clamp(_lessonSel, 0, rows.Count - 1);
        _hud.Lessons = rows.ConvertAll(r => new Hud.LessonLine(r.Header ? (r.Open ? "- " : "+ ") + r.Text : r.Text, r.Header)).ToArray();
        _hud.LessonSelection = _lessonSel;
    }
}
