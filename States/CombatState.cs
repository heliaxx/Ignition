using Godot;

public partial class CombatState : State
{
    protected Fighter Fighter => Entity as Fighter;

    public override void PhysicsUpdate(float delta)
    {
        if (Fighter == null || !Fighter.HasTarget()) return;

        if (Fighter.ShouldFlee)
            { stateMachine.TransitionTo("Flee"); return; }

        if (Fighter.ShouldEvade)
            { stateMachine.TransitionTo("Evade"); return; }

        CombatUpdate(delta);
    }

    protected virtual void CombatUpdate(float delta) {}

    // Back into the fight after breaking off, by how far away the player is.
    protected void Reengage()
    {
        if (Fighter.DistanceToPlayer() <= Fighter.WeaponRange)
            stateMachine.TransitionTo("Joust");
        else
            stateMachine.TransitionTo("Pursue");
    }
}
