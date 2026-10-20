using Godot;

public partial class Pursue : CombatState
{
    private const float THROTTLE = 1.3f;

    public override void Enter()
    {
        if (Fighter == null) return;
        // Every way into combat starts here.
        Fighter.Engaged = true;
        Fighter.TargetThrottle = THROTTLE;
        Fighter.SetLaserFiring(false);
    }

    protected override void CombatUpdate(float delta)
    {
        float distance = Fighter.DistanceToPlayer();

        if (distance <= Fighter.WeaponRange)
        {
            stateMachine.TransitionTo("Joust");
            return;
        }

        Fighter.PursuePlayer(delta, skipFacing: true);
        Fighter.FaceTarget(delta);
    }
}
