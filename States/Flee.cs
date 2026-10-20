using Godot;

// Breaks off to open the distance, then comes back
public partial class Flee : CombatState
{
    private float _fleeTimer = 0f;
    private const float MIN_FLEE_TIME = 3f;
    private const float MAX_FLEE_TIME = 5f;
    private const float THROTTLE = 1.4f;

    public override void Enter()
    {
        _fleeTimer = (float)GD.RandRange(MIN_FLEE_TIME, MAX_FLEE_TIME);
        if (Fighter == null) return;
        Fighter.HasFled = true;
        Fighter.TargetThrottle = THROTTLE;
        Fighter.SetLaserFiring(false);
    }

    public override void PhysicsUpdate(float delta)
    {
        if (Fighter == null || !Fighter.HasTarget()) return;

        _fleeTimer -= delta;
        if (_fleeTimer <= 0f)
        {
            Reengage();
            return;
        }

        Fighter.EvadePlayer(delta);
    }
}
