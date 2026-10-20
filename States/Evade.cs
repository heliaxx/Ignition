using Godot;

public partial class Evade : CombatState
{
    private float _evadeTimer = 0f;
    private const float MIN_EVADE_TIME = 2f;
    private const float MAX_EVADE_TIME = 3.5f;
    private const float THROTTLE = 1.3f;

    public override void Enter()
    {
        _evadeTimer = (float)GD.RandRange(MIN_EVADE_TIME, MAX_EVADE_TIME);
        if (Fighter == null) return;
        Fighter.TargetThrottle = THROTTLE;
        Fighter.SetLaserFiring(false);
    }

    public override void Exit()
    {
        Fighter?.HoldEvade();
    }

    public override void PhysicsUpdate(float delta)
    {
        if (Fighter == null || !Fighter.HasTarget()) return;

        // Flee check - even while evading, if health is critical, flee
        if (Fighter.ShouldFlee)
        {
            stateMachine.TransitionTo("Flee");
            return;
        }

        _evadeTimer -= delta;

        if (_evadeTimer <= 0f)
        {
            Reengage();
            return;
        }

        // Evasive maneuvers
        Fighter.EvadePlayer(delta);
    }
}
