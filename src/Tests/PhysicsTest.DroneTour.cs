using System;
using System.Linq;
using CCCPRacing.Race;
using CCCPRacing.Track;
using CCCPRacing.UI;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void DroneTourTest()
    {
        foreach (string id in new[] { "ilyich", "siberia", "atomgrad", "bonus" })
        {
            var film = DroneFilm.For(id);
            Check(film is { Ready: true } && film.Track == id, $"drone {id}: baked tour is available on the track card");
            var steps = SchoolScript.DroneFor(id);
            var lap = TrackTour.For(id, 1081);
            Check(steps.Length == 1 && lap != null && lap.Part(steps[0].step.Id) == null,
                $"drone {id}: flight step cannot be shadowed by a driving-lap part");
            Check(film.Path.All(r => r.Length == 8 && r.All(float.IsFinite) && r[2] > 0 && r[6] >= 40 && r[6] <= 70),
                $"drone {id}: finite camera samples stay above ground with a usable field of view");
            Check(film.LapAt > 0 && film.LapAt < (film.Path.Length - 1) * film.SampleStep,
                $"drone {id}: flight continues through the handover to the lap");
            Check(film.Captions.All(c => film.Marks.ContainsKey(c.mark) && c.length > 0),
                $"drone {id}: every scene caption has a flight mark and a duration");
            Check(film.CardBig.Concat(film.CardSmall).Concat(film.Captions.Select(c => c.text)).Concat(film.LapCaptions.Select(c => c.text))
                .All(t => t.All(c => c == ' ' || PixelFont.CanDraw(c))), $"drone {id}: card and captions use the game's font");
        }
        var bonus = DroneFilm.For("bonus");
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        Check(bonus.LapKeys.All(k => k.Length == 8 && k.All(float.IsFinite)) && bonus.LapKeys[0][0] == 0
            && bonus.LapKeys[^1][0] >= track.Center.Length - 5,
            "drone bonus: chase keys cover the revised Poligon lap including its finish");
        Check(bonus.LapCaptions.All(c => c.from >= 0 && c.from < c.to && c.to <= track.Center.Length),
            "drone bonus: all driving tips lie on the revised lap");
        Check(bonus.Marks.Keys.Contains("hall") && bonus.Marks.Keys.Contains("tanks") && bonus.Marks.Keys.Contains("battery")
            && bonus.Marks.Keys.Contains("helipads"), "drone bonus: flight includes the hall and military landmarks");
    }
}
