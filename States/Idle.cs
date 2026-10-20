using Godot;

public partial class Idle : CombatState
{
    [Export] public float MaxWanderRadius = 1000f;

    private float detectionRangeSquared;
    private float maxWanderRadiusSquared;
    private ReturnToCenter _returnToCenter;
    private const float THROTTLE = 0.6f;

    public override void Enter()
    {
        if (Fighter != null) Fighter.TargetThrottle = THROTTLE;
        float range = Fighter?.DetectionRange ?? 600f;
        detectionRangeSquared = range * range;
        maxWanderRadiusSquared = MaxWanderRadius * MaxWanderRadius;
        _returnToCenter = stateMachine.GetNodeOrNull<ReturnToCenter>("ReturnToCenter");
        Fighter?.SetLaserFiring(false);
    }

    public override void PhysicsUpdate(float delta)
    {
        if (Fighter == null || !Fighter.HasTarget()) return;

        Fighter.Wander(delta);

        // A hit from beyond detection range engages it too, coming at the shooter rather than dodging.
        if (Fighter.TookRecentDamage)
        {
            Fighter.HoldEvade();
            stateMachine.TransitionTo("Pursue");
            return;
        }

        if (_returnToCenter != null)
        {
            float distSquared = Fighter.GlobalPosition.DistanceSquaredTo(_returnToCenter.CenterPoint);
            if (distSquared > maxWanderRadiusSquared)
            {
                stateMachine.TransitionTo("ReturnToCenter");
                return;
            }
        }

        if (Fighter.DistanceSquaredToPlayer() < detectionRangeSquared)
            stateMachine.TransitionTo("Pursue");
    }
}
