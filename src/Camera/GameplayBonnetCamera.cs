using Godot;

namespace CCCPRacing.Camera;

/// <summary>A body-mounted view at the very front edge of the bonnet: the bonnet is behind the lens, so none of it (nor its damage geometry) is in frame.</summary>
internal static class GameplayBonnetCamera
{
    public static (Vector3 eye, Vector3 target) Frame(Vector3 car, Basis body)
    {
        // The Pyatyorka body frame: the bonnet's top is at Y=0.284 and its front edge at Z=-1.946 (grille -1.969, bumper -2.03, both far below the view).
        // The lens sits just ahead of that edge and a little above it, without a lag that could enter the body.
        Vector3 eye = car + body * new Vector3(0, 0.42f, -1.96f);
        return (eye, eye - body.Z * 40f - body.Y * 0.5f);
    }
}
