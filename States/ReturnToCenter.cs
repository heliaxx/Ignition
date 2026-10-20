using Godot;
using System;

public partial class ReturnToCenter : CombatState
{
    [Export] public Vector3 CenterPoint = Vector3.Zero;
    [Export] public float AcceptableRadius = 50f;
    private float acceptableRadiusSquared;
    private const float THROTTLE = 0.8f;

    public override void Enter()
    {
        if (Fighter != null) Fighter.TargetThrottle = THROTTLE;
        acceptableRadiusSquared = AcceptableRadius * AcceptableRadius;
    }

    public override void PhysicsUpdate(float delta)
    {
        if (Fighter == null) return;

        float detectionSq = Fighter.DetectionRange * Fighter.DetectionRange;
        if (Fighter.DistanceSquaredToPlayer() < detectionSq)
        {
            stateMachine.TransitionTo("Pursue");
            return;
        }

        // As in Idle: a hit from beyond detection range engages it, coming at the shooter.
        if (Fighter.TookRecentDamage)
        {
            Fighter.HoldEvade();
            stateMachine.TransitionTo("Pursue");
            return;
        }

        Vector3 toCenter = CenterPoint - Fighter.GlobalPosition;
        if (toCenter.LengthSquared() < acceptableRadiusSquared)
        {
            stateMachine.TransitionTo("Idle");
            return;
        }

        Fighter.MoveInDirection(toCenter.Normalized(), delta, skipFacing: true);
        Fighter.FaceDirection(toCenter, delta);
    }
}
