using Godot;

// Base for weapon nodes, with optional gimbal tracking. Mount at the barrel
// position; the weapon's -Z axis is the firing direction.
public abstract partial class WeaponBase : Node3D
{
    [ExportGroup("Gimbal")]
    [Export] public bool HasGimbal = false;
    [Export] public float GimbalAngle = 10f;   // max deviation from mount forward, degrees
    [Export] public float GimbalSpeed = 10f;    // tracking speed (higher = snappier)

    protected Node3D _target;

    // Override in subclasses for lead prediction. Laser = instant (MaxValue).
    protected virtual float GimbalLeadSpeed => float.MaxValue;

    // Velocity a shot carries from its launcher. It already moves with the shooter, so only
    // the target's motion relative to the shooter needs leading.
    protected virtual Vector3 InheritedLaunchVelocity => Vector3.Zero;

    // Easing alone trails a target that keeps moving, by its angular rate over GimbalSpeed,
    // which at a few hundred metres is wider than a ship. Below this rate the barrel keeps up.
    private static readonly float MinTrackRate = Mathf.DegToRad(90f);

    // Weapon's initial aim direction stored in parent-local space on first gimbal update.
    // This tracks the "neutral" barrel direction even as the ship rotates.
    private Vector3? _neutralLocal;

    public void SetTarget(Node3D target) => _target = target;

    public abstract void StartFiring();
    public abstract void StopFiring();

    // Rotates toward the target within the gimbal cone. Call from _PhysicsProcess.
    protected void UpdateGimbal(float delta)
    {
        if (!HasGimbal) return;

        var parent = GetParentNode3D();
        if (parent == null) return;

        // Lazily capture the weapon's initial aim direction in parent-local space.
        // Using parent-local ensures it correctly tracks the ship's rotation.
        if (!_neutralLocal.HasValue)
            _neutralLocal = (parent.GlobalTransform.Basis.Inverse() * (-GlobalTransform.Basis.Z)).Normalized();

        Vector3 mountFwd = (parent.GlobalTransform.Basis * _neutralLocal.Value).Normalized();

        // Aim at target when one exists, otherwise return to mount forward.
        Vector3 targetDir = GetIdealAimDir() ?? mountFwd;

        // Clamp to gimbal cone around mount forward.
        float limitRad = Mathf.DegToRad(GimbalAngle);
        if (mountFwd.AngleTo(targetDir) > limitRad)
        {
            Vector3 clampAxis = mountFwd.Cross(targetDir);
            targetDir = clampAxis.LengthSquared() > 1e-4f
                ? mountFwd.Rotated(clampAxis.Normalized(), limitRad).Normalized()
                : mountFwd;
        }

        // Smoothly rotate current aim toward target. Step is capped at the
        // remaining angle so it lands exactly on target instead of easing
        // toward it forever (which left the reticle slightly off-center).
        Vector3 cur = -GlobalTransform.Basis.Z;
        float remaining = cur.AngleTo(targetDir);
        if (remaining < 1e-4f) return;

        float step = Mathf.Max(remaining * (1f - Mathf.Exp(-GimbalSpeed * delta)), MinTrackRate * delta);
        Vector3 newFwd = remaining > 1e-3f
            ? cur.Slerp(targetDir, Mathf.Min(step / remaining, 1f)).Normalized()
            : targetDir;
        LookAt(GlobalPosition + newFwd, parent.GlobalTransform.Basis.Y);
    }

    private Vector3? GetIdealAimDir()
    {
        if (_target == null || !IsInstanceValid(_target)) return null;

        Vector3 tp = _target.GlobalPosition;

        if (GimbalLeadSpeed < 1e9f)
        {
            Vector3 tv = ((_target as GimbalTarget)?.GetVelocity() ?? Vector3.Zero) - InheritedLaunchVelocity;
            tp = AimUtils.PredictIntercept(GlobalPosition, tp, tv, GimbalLeadSpeed);
        }

        return (tp - GlobalPosition).Normalized();
    }

    // Projects the current aim direction onto the screen.
    public Vector2? GetAimScreenPos()
    {
        var cam = GetViewport()?.GetCamera3D();
        if (cam == null) return null;
        Vector3 pt = GlobalPosition + (-GlobalTransform.Basis.Z) * 500f;
        if (cam.IsPositionBehind(pt)) return null;
        return cam.UnprojectPosition(pt);
    }

    // Gimbal cone radius in screen pixels.
    public float GetGimbalScreenRadius()
    {
        var cam = GetViewport()?.GetCamera3D();
        if (cam == null) return 0f;
        float h = GetViewport().GetVisibleRect().Size.Y;
        return Mathf.Tan(Mathf.DegToRad(GimbalAngle))
             / Mathf.Tan(Mathf.DegToRad(cam.Fov) * 0.5f)
             * (h * 0.5f);
    }
}
