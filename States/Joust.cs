using Godot;

// Strafing runs. Each run aims StandoffDistance beside the player rather than at them, so the
// pass goes past firing instead of into a ram, with the nose tracking the player. Once past,
// the fighter either settles into an orbit or extends straight out to get room and turns in
// for the next run: a long straight approach is what gives the nose time to line up.
public partial class Joust : CombatState
{
    // Eases off while the player sits in the firing arc to keep them there longer,
    // and opens up when they slip out of it.
    private const float ON_TARGET_THROTTLE = 0.8f;
    private const float OFF_TARGET_THROTTLE = 1.1f;
    private const float EXTEND_THROTTLE = 1.2f;

    // How far out, as a share of WeaponRange, the fighter extends before turning in again.
    private const float EXTEND_RANGE = 0.75f;

    // Chance that a finished pass turns into an orbit instead of another run.
    private const float ORBIT_CHANCE = 0.5f;

    // Which side of the player this run passes, fixed in the world for the run. Kept square to
    // the line of sight instead, the aim point would swing round with the fighter and it would
    // circle the player rather than ever pass them.
    private Vector3 _side;
    private bool _extending;
    // Set while flying a run, so only a real pass can end in an orbit, not a Joust entered
    // already heading away, as it is after an evade or an orbit.
    private bool _running;

    public override void Enter()
    {
        if (Fighter == null) return;
        Fighter.SetLaserFiring(false);
        _extending = false;
        _running = false;
        PickSide();
    }

    public override void Exit()
    {
        Fighter?.SetLaserFiring(false);
    }

    protected override void CombatUpdate(float delta)
    {
        float distance = Fighter.DistanceToPlayer();

        if (distance > Fighter.WeaponRange * 1.2f)
        {
            stateMachine.TransitionTo("Pursue");
            return;
        }

        if (distance < Fighter.CloseRange)
        {
            stateMachine.TransitionTo("Orbit");
            return;
        }

        bool movingAway = Fighter.Velocity.Dot(Fighter.ToPlayerVector()) < 0f;
        if (movingAway && distance < Fighter.WeaponRange * EXTEND_RANGE)
        {
            if (_running)
            {
                _running = false;
                if (GD.Randf() < ORBIT_CHANCE)
                {
                    stateMachine.TransitionTo("Orbit");
                    return;
                }
            }
            Extend(delta);
            return;
        }

        if (_extending)
        {
            _extending = false;
            PickSide();
        }

        _running = true;
        Fighter.PursuePlayer(delta, skipFacing: true, offset: _side * Fighter.StandoffDistance);
        Fighter.FaceTarget(delta);
        bool onTarget = Fighter.IsPlayerInFiringArc();
        Fighter.TargetThrottle = onTarget ? ON_TARGET_THROTTLE : OFF_TARGET_THROTTLE;
        Fighter.SetLaserFiring(onTarget);
    }

    // Straight on along the current heading, nose forward, guns cold.
    private void Extend(float delta)
    {
        _extending = true;
        Fighter.SetLaserFiring(false);
        Fighter.TargetThrottle = EXTEND_THROTTLE;

        Vector3 heading = Fighter.Velocity.LengthSquared() > 1f ? Fighter.Velocity : Fighter.GetForward();
        Fighter.MoveInDirection(heading, delta);
    }

    private void PickSide()
    {
        Vector3 toPlayer = Fighter.ToPlayerVector().Normalized();
        Vector3 reference = Mathf.Abs(toPlayer.Y) > 0.99f ? Vector3.Right : Vector3.Up;
        Vector3 perpendicular = toPlayer.Cross(reference).Normalized();
        _side = perpendicular.Rotated(toPlayer, GD.Randf() * Mathf.Tau);
    }
}
