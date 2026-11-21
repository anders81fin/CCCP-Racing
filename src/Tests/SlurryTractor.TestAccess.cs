namespace CCCPRacing.Track;

// Development only: the slurry tractor's revenge reaches the road in the real game after the fate's trigger; the situation scenario raises the same event to see the message that follows it.
public partial class SlurryTractor
{
    public void RaiseRevengeOnTheRoadForTest() => RevengeOnTheRoad?.Invoke();
}
