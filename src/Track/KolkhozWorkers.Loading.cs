using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

public partial class KolkhozWorkers
{
    /// <summary>Stationary loading crews beside Ilyich's church store and north grain silo.</summary>
    public static IEnumerable<KolkhozWorkerSpec> LoadingCrew(TrackData t)
    {
        if (t.Id != "ilyich") yield break;

        // The churchyard workers stay south of the trailer and west of the door. This leaves
        // the 46.2,183.8 -> 49,191.2 gate-to-door wheel track open for traffic and pedestrians.
        yield return new KolkhozWorkerSpec(new Vector2(43.9f, 190.4f), -2.56f, KolkhozJob.CarrySack, 0, 0.2f);
        yield return new KolkhozWorkerSpec(new Vector2(45.2f, 188.9f), -2.86f, KolkhozJob.CarrySack, 1, 1.1f);
        yield return new KolkhozWorkerSpec(new Vector2(46.5f, 189.9f), -2.91f, KolkhozJob.Pitchfork, 2, 2.0f);
        yield return new KolkhozWorkerSpec(new Vector2(46.8f, 196.0f), 0.08f, KolkhozJob.Load, 3, 0.7f);
        yield return new KolkhozWorkerSpec(new Vector2(43.7f, 187.8f), -2.18f, KolkhozJob.Foreman, 4, 1.7f);

        // A second working group gathers around the north grain silo and the red barn's east
        // apron, outside both building footprints and clear of the road leading into the field.
        yield return new KolkhozWorkerSpec(new Vector2(149.8f, 33.8f), -2.55f, KolkhozJob.CarrySack, 0, 0.4f);
        yield return new KolkhozWorkerSpec(new Vector2(156.5f, 36.4f), 1.78f, KolkhozJob.Load, 1, 1.3f);
        yield return new KolkhozWorkerSpec(new Vector2(151.8f, 43.0f), 0.05f, KolkhozJob.Pitchfork, 2, 2.2f);
        yield return new KolkhozWorkerSpec(new Vector2(148.5f, 40.1f), -1.8f, KolkhozJob.Foreman, 3, 0.9f);
    }
}
