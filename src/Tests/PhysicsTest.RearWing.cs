using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>The rear wing of the fuel: one of three heights shows (none for the diesel) and a hard rear hit tears off the worn one only.</summary>
    void RearWingTest()
    {
        var parameters = new CarParams();
        var car = new CarSim(parameters, new FlatGround()); car.Reset(new(0, 2, 0), 0);
        var visual = CarVisual.Create(parameters, Colors.Red); AddChild(visual);
        var ghost = CarVisual.Create(parameters, Colors.Red, ghost: true); AddChild(ghost);
        var fx = new TyreEffects(); fx.Init(TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"))); AddChild(fx);
        var damage = visual.Damage;
        Check(damage != null, "rear wing: the production car has its damage model");
        if (damage == null) return;
        MeshInstance3D Node(string height) => damage.GetNodeOrNull<MeshInstance3D>("rear wing " + height);
        string[] heights = { "low", "mid", "high", "duck" };
        bool one = true, none = true, ghostOne = true;
        for (int wing = -1; wing <= 3; wing++)
        {
            visual.Wing = wing;
            for (int h = 0; h < 4; h++) one &= Node(heights[h]) != null && Node(heights[h]).Visible == (h == wing);
            for (int h = 0; h < 4; h++) none &= wing >= 0 || !Node(heights[h]).Visible;
            // the ghost's body mesh carries only the worn wing's surfaces
            int wingSurfaces = 0;
            ghost.Wing = wing;
            var gm = FirstBody(ghost)?.Mesh;
            for (int s = 0; gm != null && s < gm.GetSurfaceCount(); s++)
                if ((gm.SurfaceGetMaterial(s)?.ResourceName ?? "").StartsWith("Wing_")) wingSurfaces++;
            ghostOne &= wingSurfaces == (wing < 0 ? 0 : wing == 3 ? 1 : 2);
        }
        Check(one, "rear wing: only the worn height is shown");
        Check(none, "rear wing: none (-1) hides every wing");
        Check(ghostOne, "rear wing: the ghost body carries one wing (two surfaces, the duck one) or none");

        // What the fuels wear besides the wing: bumpers (not JET FUEL, SPUTNIK), roof rack with the spare (not SPUTNIK), the SPUTNIK's kit.
        MeshInstance3D Part(string name) => damage.GetNodeOrNull<MeshInstance3D>(name);
        visual.Equip(true, true, false);
        Check(Part("front bumper").Visible && Part("rear bumper").Visible && Part("roof rack").Visible && !Part("sputnik kit").Visible && Part("twin exhaust").Visible, "equipment: petrol and diesel wear bumpers, the rack and twin pipes, no kit");
        visual.Equip(false, true, false);
        Check(!Part("front bumper").Visible && !Part("rear bumper").Visible && Part("roof rack").Visible, "equipment: jet fuel runs without bumpers, with the rack");
        visual.Equip(false, false, true);
        Check(!Part("roof rack").Visible && Part("sputnik kit").Visible && !Part("twin exhaust").Visible, "equipment: the sputnik has no rack and no twin pipes, but its kit");
        // The tail pipes: the petrol the body's twin pipes, the diesel one wide pipe, the jet fuel four; the sputnik's kit hides them all. The built ones dent and fall off in a hard hit from behind.
        visual.Equip(true, true, false, CarDamage.PipeSet.Twin);
        Check(Part("twin exhaust").Visible && !Part("single exhaust").Visible && !Part("quad exhaust").Visible, "pipes: the petrol wears the body's twin pipes only");
        visual.Equip(true, true, false, CarDamage.PipeSet.Single);
        Check(!Part("twin exhaust").Visible && Part("single exhaust").Visible && !Part("quad exhaust").Visible, "pipes: the diesel wears one pipe");
        visual.Equip(false, true, false, CarDamage.PipeSet.Quad);
        Check(!Part("twin exhaust").Visible && !Part("single exhaust").Visible && Part("quad exhaust").Visible, "pipes: the jet fuel wears four");
        visual.Equip(false, false, true, CarDamage.PipeSet.Quad);
        Check(!Part("twin exhaust").Visible && !Part("single exhaust").Visible && !Part("quad exhaust").Visible, "pipes: the sputnik's kit hides them all");
        Check(damage.BodyTriangles("Pipe1_", "Pipe4_") == 0, "pipes: the built pipes' triangles are all in their parts, none in the body");
        damage.Reset();
        visual.Equip(true, true, false, CarDamage.PipeSet.Single);
        car.Pos = new(0, 2, 0);
        for (int k = 0; k < 4; k++) damage.Hit(24f + k, new(0.4f, -0.4f, 2.05f), Vector3.Forward, car, fx);
        Check(Part("single exhaust").TopLevel && !Part("quad exhaust").TopLevel, "pipes: a hard hit from behind tears the diesel's pipe off");
        damage.Reset();
        Check(!Part("single exhaust").TopLevel && Part("single exhaust").Visible, "pipes: the restart puts the pipe back");
        visual.Equip(false, true, false, CarDamage.PipeSet.Quad);
        for (int k = 0; k < 4; k++) damage.Hit(24f + k, new(0.4f, -0.4f, 2.05f), Vector3.Forward, car, fx);
        Check(Part("quad exhaust").TopLevel && !Part("single exhaust").TopLevel, "pipes: and the jet fuel's four");
        damage.Reset();
        visual.Equip(true, true, false);
        car.Pos = new(0, 2, 0);
        for (int k = 0; k < 4; k++) damage.Hit(24f + k, new(0, 0.5f, 0), Vector3.Forward, car, fx);
        Check(!Part("sputnik kit").TopLevel, "equipment: a hit never tears the kit off");
        Check(!Part("front bumper").TopLevel, "equipment: a missing bumper is not torn off");
        damage.Reset();
        visual.Equip(true, true, false);
        for (int k = 0; k < 4; k++) damage.Hit(24f + k, new(0, 0.95f, 0.1f), Vector3.Down, car, fx);
        Check(Part("roof rack").TopLevel, "equipment: a hard hit on the roof tears the rack and the spare off");
        damage.Reset();
        Check(!Part("roof rack").TopLevel && Part("roof rack").Visible, "equipment: the restart puts the rack back");
        // a side hit at door height leaves the rack alone
        for (int k = 0; k < 4; k++) damage.Hit(24f + k, new(0.9f, 0f, 0.1f), Vector3.Left, car, fx);
        Check(!Part("roof rack").TopLevel, "equipment: a side hit at the doors does not take the rack");
        damage.Reset();
        visual.Equip(true, true, false);

        Check(damage.BodyTriangles("Wing_", "Kit_", "Pipe_", "Tyre", "Steel_Wheel") == 0, "rear wing: every wing, kit and spare triangle went into its part, none stays in the body (a stray one would show on every car)");
        visual.Wing = 1;
        Check(!damage.IsDamaged, "rear wing: a clean car with hidden wings is not damaged");
        car.Pos = new(0, 2, 0);
        for (int k = 0; k < 4; k++) damage.Hit(24f + k, new(0, 0.3f, 1.8f), Vector3.Forward, car, fx);
        var mid = Node("mid");
        Check(mid.TopLevel, "rear wing: a hard rear hit tears the worn wing off");
        Check(!Node("low").TopLevel && !Node("high").TopLevel && !Node("duck").TopLevel, "rear wing: the hidden wings are not torn off");
        damage.Reset();
        visual.Wing = 3;
        for (int k = 0; k < 4; k++) damage.Hit(24f + k, new(0, 0.3f, 1.8f), Vector3.Forward, car, fx);
        Check(Node("duck").TopLevel && !Node("mid").TopLevel, "rear wing: the diesel's duck-tail lip tears off too");
        damage.Reset();
        visual.Wing = 1;
        Check(!mid.TopLevel && mid.Visible && !Node("high").Visible && !Node("duck").Visible, "rear wing: the restart puts the wing back and keeps the others hidden");
    }

    static MeshInstance3D FirstBody(Node n)
    {
        foreach (var c in n.GetChildren())
        {
            if (c is MeshInstance3D m && m.Mesh != null && m.Mesh.GetSurfaceCount() > 10) return m;
            if (FirstBody(c) is MeshInstance3D r) return r;
        }
        return null;
    }
}
