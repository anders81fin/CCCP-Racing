using System;
using System.Collections.Generic;
using System.Linq;
namespace CCCPRacing.Replay;

/// <summary>Pins come first across both archives; each group follows capture order despite wall-clock changes.</summary>
public static class LapListOrder
{
    public static IOrderedEnumerable<LapEntryInfo> CurrentRun(IEnumerable<LapEntryInfo> rows,IReadOnlyDictionary<string,int> captured)
        => rows.OrderByDescending(i=>i.Pinned)
            .ThenBy(i=>captured.TryGetValue(i.Id,out int n)?n:int.MaxValue)
            .ThenBy(i=>i.Meta.LapNumber>0?i.Meta.LapNumber:int.MaxValue)
            .ThenBy(i=>i.Meta.RecordedUtc).ThenBy(i=>i.Id,StringComparer.Ordinal);
}
