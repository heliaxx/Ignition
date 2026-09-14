using Godot;

public enum GimbalTargetType { Enemy, Ally, Powerup }

// Makes its position targetable by the player's gimbal. Joins the "gimbal_targets"
// group on ready, uses its parent as owner, and forwards the parent's Died signal.
public partial class GimbalTarget : Node3D
{
	[Export] public GimbalTargetType TargetType = GimbalTargetType.Enemy;

	// Overrides the HUD name; falls back to parent name, then node name.
	[Export] public string DisplayNameOverride = "";

	[Signal]
	public delegate void DiedEventHandler();

	private Node3D _owner;
	public Node3D TargetOwner => _owner;

	public override void _Ready()
	{
		AddToGroup("gimbal_targets");
		_owner = GetParentOrNull<Node3D>();

		// Forward the parent's Died signal as our own, if it has one
		if (_owner != null && _owner.HasSignal("Died"))
			_owner.Connect("Died", Callable.From(() => EmitSignal(SignalName.Died)));
	}

	// Velocity of the parent physics body, or zero.
	public Vector3 GetVelocity()
	{
		if (_owner is CharacterBody3D cb && IsInstanceValid(cb)) return cb.Velocity;
		if (_owner is RigidBody3D rb    && IsInstanceValid(rb)) return rb.LinearVelocity;
		return Vector3.Zero;
	}

	// True if this target can still be locked (visible, alive, enabled).
	public bool IsValid()
	{
		if (!IsInstanceValid(this) || !Visible)
			return false;

		if (_owner == null || !IsInstanceValid(_owner))
			return true; 

		if (!_owner.Visible || _owner.ProcessMode == ProcessModeEnum.Disabled)
			return false;

		if (_owner is Fighter fighter)
			return fighter.CurrentHealthValue > 0;

		return true;
	}

	public string GetDisplayName()
	{
		if (!string.IsNullOrEmpty(DisplayNameOverride)) return DisplayNameOverride;
		if (_owner is Fighter fighter) return fighter.DisplayName;
		return _owner?.Name ?? Name;
	}

	// True if the owner exposes health for the target HUD bar.
	public bool HasHealthData() => IsInstanceValid(_owner) && _owner is Fighter or PlayerShip;

	public float GetCurrentHealth() => _owner switch
	{
		Fighter fighter => fighter.CurrentHealthValue,
		PlayerShip ship => ship.CurrentHealth,
		_ => 0,
	};

	public float GetMaxHealth() => _owner switch
	{
		Fighter fighter => fighter.MaxHealthValue,
		PlayerShip ship => ship.MaxHealth,
		_ => 1,
	};
}
