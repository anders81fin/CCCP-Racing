using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

public partial class KolkhozWorkers
{
    /// <summary>Workers spread through the fields, with at least eight metres between work sites.</summary>
    public static IEnumerable<KolkhozWorkerSpec> FieldCrew(TrackData t)
    {
        if (t.Id != "ilyich") yield break;
        static KolkhozWorkerSpec At(float x, float z, float faceX, float faceZ,
            KolkhozJob job, int variant, float phase) =>
            new(new Vector2(x, z), TrackData.YawFor(new Vector2(faceX, faceZ)), job, variant, phase);

        yield return At(114, 278, 1, 0.2f, KolkhozJob.Reap, 0, 0.2f);
        yield return At(125, 290, -0.4f, 1, KolkhozJob.Reap, 2, 1.1f);
        yield return At(304, 215, 1, 0.2f, KolkhozJob.Reap, 1, 0.6f);
        yield return At(310, 226, -1, 0.4f, KolkhozJob.Reap, 3, 1.5f);
        yield return At(162, 274, 0.2f, 1, KolkhozJob.Reap, 2, 0.9f);
        yield return At(177, 285, -1, -0.3f, KolkhozJob.Reap, 5, 1.8f);
        yield return At(148, 243, 1, 0.2f, KolkhozJob.CarrySack, 5, 1.3f);

        // Independent jobs across the whole harvested north field, rather than a close row at one bale.
        yield return At(126, 112, 1, 1, KolkhozJob.Pitchfork, 2, 0.4f);
        yield return At(143, 96, 1, 0.3f, KolkhozJob.CarrySack, 4, 2.2f);
        yield return At(164, 112, -1, 0.4f, KolkhozJob.Load, 5, 1.3f);
        yield return At(191, 96, 1, 1, KolkhozJob.Pitchfork, 3, 2.8f);
        yield return At(215, 136, -0.4f, 1, KolkhozJob.Load, 5, 2.4f);
        yield return At(239, 151, -1, 0, KolkhozJob.Foreman, 3, 0.7f);
        yield return At(154, 139, 0.5f, -0.8f, KolkhozJob.Rest, 1, 1.7f);
    }
}
