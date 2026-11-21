namespace CCCPRacing.Race;

// Development only: a lap that starts (or is rescued) is not following the road yet; the first road it touches may be far ahead, and the timer then says SHORTCUT. The situation scenario puts the timer in that state.
public partial class LapTimer
{
    public void UnfollowForTest() => _followed = false;
}
