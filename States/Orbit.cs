using Godot;

// Circles the player at OrbitDistance until the timer runs out, then transitions to Joust.
// Changes orbit direction every few seconds.
public partial class Orbit : CombatState
{
    private float _orbitDirection = 1f; // 1 = right, -1 = left
    private float _directionChangeTimer = 0f;
    private const float MIN_DIRECTION_CHANGE = 2f;
    private const float MAX_DIRECTION_CHANGE = 5f;
    // Slower turns tighter, which keeps the player in view at close range.
    private const float THROTTLE = 0.7f;
    private const float RADIUS_CORRECTION = 50f;
    private const float MIN_ORBIT_TIME = 6f;
    private const float MAX_ORBIT_TIME = 10f;
    private float _orbitTimer;

    // Normal of the plane it circles in, fixed for the orbit so the circle holds still.
    private Vector3 _axis;

    public override void Enter()
    {
        if (Fighter == null) return;
        Fighter.TargetThrottle = THROTTLE;
        _orbitDirection = 1f;
        _orbitTimer = (float)GD.RandRange(MIN_ORBIT_TIME, MAX_ORBIT_TIME);
        _directionChangeTimer = (float)GD.RandRange(MIN_DIRECTION_CHANGE, MAX_DIRECTION_CHANGE);
        Fighter.SetLaserFiring(false);
        PickAxis();
    }

    public override void Exit()
    {
        Fighter?.SetLaserFiring(false);
    }

    protected override void CombatUpdate(float delta)
    {
        float distance = Fighter.DistanceToPlayer();

        _orbitTimer -= delta;
        if (_orbitTimer <= 0f || distance > Fighter.OrbitDistance * 2f)
        {
            stateMachine.TransitionTo("Joust");
            return;
        }

        _directionChangeTimer -= delta;
        if (_directionChangeTimer <= 0f)
        {
            _orbitDirection = -_orbitDirection;
            _directionChangeTimer = (float)GD.RandRange(MIN_DIRECTION_CHANGE, MAX_DIRECTION_CHANGE);
        }

        Vector3 radial = -Fighter.ToPlayerVector() / distance;
        Vector3 axis = _axis - radial * _axis.Dot(radial);
        if (axis.LengthSquared() > 0.0001f) _axis = axis.Normalized();
        else PickAxis();

        Vector3 tangent = _axis.Cross(radial) * _orbitDirection;
        float correction = Mathf.Clamp((Fighter.OrbitDistance - distance) / RADIUS_CORRECTION, -1f, 1f);
        // Carried along with the player too, so the circle stays centred on them.
        Vector3 direction = tangent + radial * correction + Fighter.GetPlayerVelocity() / Fighter.Speed;

        Fighter.MoveInDirection(direction, delta, skipFacing: true);
        Fighter.FaceTarget(delta);
        Fighter.SetLaserFiring(Fighter.IsPlayerInFiringArc());
    }

    // The plane it is already moving in, so the orbit picks up from its current heading.
    private void PickAxis()
    {
        Vector3 radial = -Fighter.ToPlayerVector().Normalized();
        Vector3 axis = radial.Cross(Fighter.Velocity);
        if (axis.LengthSquared() < 1f)
            axis = radial.Cross(Mathf.Abs(radial.Y) > 0.99f ? Vector3.Right : Vector3.Up);
        _axis = axis.Normalized();
    }
}
