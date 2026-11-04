using System.Collections.Generic;
using Godot;

namespace CCCPRacing.UI;

/// <summary>How much a message matters to the game (docs/comments.csv has them all).</summary>
public enum Prio
{
    /// <summary>The plan, the car's fate, dangers, key presses: always shown, at once.</summary>
    Critical = 1,
    /// <summary>Useful news and how-tos: waits while a Critical or Important one is up.</summary>
    Important = 2,
    /// <summary>Jokes: only after QuietGap s with nothing on screen, never in a dangerous moment, off with TAB.</summary>
    Nice = 3,
}

/// <summary>
/// The race's messages in two places: the comment in the middle (Show) and the alert above it
/// (Alert). One rule for both (2026-10-05): a Critical message always shows and pushes jokes off
/// the screen; an Important one pushes jokes off but waits in a queue (dropped after QueueLife s)
/// while another Critical or Important one is up, or is dropped at once when it is urgent (a
/// warning is useless late); a Nice one shows only when the screen has been quiet for QuietGap s,
/// nothing dangerous is going on (Danger) and the comments are on. While Frozen (pause, a stop
/// card, the name entry) the clocks stand still, so nothing expires unseen behind a card.
/// </summary>
public partial class Hud
{
    public const float QuietGap = 10f, QueueLife = 8f;

    class Message
    {
        public string Text, Sub;
        public Color Color;
        public float Time, Duration;
        public Prio Prio;
        public bool Top;          // the alert line (else the comment in the middle)
        public bool Ages;         // posted while frozen (a key's confirmation): its clock runs anyway
        public float Waited;      // in the queue
    }

    Message _center, _top;
    readonly List<Message> _queue = new();
    float _quiet = QuietGap;
    float _startReservation;

    /// <summary>Keep countdown, GO and the opening norm readable; real dangers still interrupt.</summary>
    public void ReserveStartMessages(float seconds)
    {
        ClearMessages();
        ClearAlert();
        _quiet = 0;
        _startReservation = seconds;
    }

    /// <summary>The game stands still (pause, a stop card, the name entry): message clocks stop too.</summary>
    public bool Frozen;
    /// <summary>Something dangerous is going on (a missile, the tanks' revenge, the norm's last lap, low fuel): no jokes.</summary>
    public bool Danger;

    /// <summary>A comment in the middle of the screen.</summary>
    /// <param name="plain">What it says with the comments off (TAB): the news without the joke (null: the message as it is).</param>
    /// <param name="plainSub">The plain version's second line.</param>
    public void Show(string text, string sub = null, float duration = 2.5f, Color? color = null, Prio prio = Prio.Important, bool urgent = false,
        string plain = null, string plainSub = null)
    {
        if (!Comments && plain != null) { text = plain; sub = plainSub; }
        Post(new Message { Text = text, Sub = sub, Color = color ?? Gold, Duration = duration, Prio = prio }, urgent);
    }

    /// <summary>A comment that shows whatever else is up and with the comments off (confirmations of a key press).</summary>
    public void ShowAlways(string text, string sub = null, float duration = 2.5f, Color? color = null)
        => Show(text, sub, duration, color, Prio.Critical);

    /// <summary>The alert line above the comments.</summary>
    public void Alert(string text, string sub, float duration, Color color, Prio prio = Prio.Critical, bool urgent = false)
        => Post(new Message { Text = text, Sub = sub, Color = color, Duration = duration, Prio = prio, Top = true }, urgent);

    public void ClearMessages()
    {
        _center = null;
        _queue.RemoveAll(m => !m.Top);
    }

    public void ClearAlert()
    {
        _top = null;
        _queue.RemoveAll(m => m.Top);
    }

    bool Busy => _center is { Prio: <= Prio.Important } || _top is { Prio: <= Prio.Important };

    static readonly bool MsgDbg = OS.HasEnvironment("MSGDBG");

    void Post(Message m, bool urgent)
    {
        m.Ages = Frozen;
        if (_startReservation > 0 && m.Prio != Prio.Critical) { Log("dropped", m); return; }
        switch (m.Prio)
        {
            case Prio.Nice:
                if (!Comments || Danger || _center != null || _top != null || _quiet < QuietGap) { Log("dropped", m); return; }
                break;
            case Prio.Important when Busy:
                if (!urgent && _queue.Count < 4) { _queue.Add(m); Log("queued", m); }
                else Log("dropped", m);
                return;
        }
        Log("shown", m);
        Place(m);
    }

    void Log(string what, Message m)
    {
        if (MsgDbg) GD.Print($"msg: {what} [{(int)m.Prio}{(m.Top ? " top" : "")}] {m.Text}{(Danger ? " (danger)" : "")}");
    }

    void Place(Message m)
    {
        if (m.Top) _top = m; else _center = m;
        // Anything that matters pushes a joke off the other line.
        if (m.Prio <= Prio.Important)
        {
            if (_center is { Prio: Prio.Nice }) _center = null;
            if (_top is { Prio: Prio.Nice }) _top = null;
        }
        _quiet = 0;
    }

    /// <summary>Tests: the texts on the two lines now, and the clock.</summary>
    public string CenterText => _center?.Text;
    public string TopText => _top?.Text;
    public void TickMessagesForTest(float dt) => TickMessages(dt);

    void TickMessages(float dt)
    {
        foreach (var m in new[] { _center, _top })
            if (m != null && (!Frozen || m.Ages)) m.Time += dt;
        if (_center != null && _center.Time > _center.Duration) _center = null;
        if (_top != null && _top.Time > _top.Duration) _top = null;
        if (Frozen) return;
        _startReservation = System.Math.Max(0, _startReservation - dt);
        if (_center == null && _top == null) _quiet += dt;
        foreach (var q in _queue) q.Waited += dt;
        _queue.RemoveAll(q => q.Waited > QueueLife);
        if (_queue.Count > 0 && !Busy)
        {
            var next = _queue[0];
            _queue.RemoveAt(0);
            Place(next);
        }
    }
}
