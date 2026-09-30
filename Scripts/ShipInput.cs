using Godot;

// One tick's worth of intent for a ship: what the flight model needs, nothing else.
// Input.IsActionPressed only answers "is it held right now", so it cannot drive a tick
// re-run during reconciliation or a ship somebody else is flying. Pitch/Yaw/Roll are the
// output of aiming, not raw mouse movement — sensitivity and smoothing stay local.
public struct ShipInput
{
	public float Pitch;   // -1..1
	public float Yaw;     // -1..1
	public float Roll;    // -1..1

	public bool ThrustForward;
	public bool ThrustBackward;
	public bool StrafeUp;
	public bool StrafeDown;
	public bool StrafeLeft;
	public bool StrafeRight;
	public bool Stop;
	public bool FlightAssist;
	public bool Partial; // assists only rotation, the ship drifts
	public float Throttle; // -1..1

	// Held, not edges: IsActionJustPressed cannot be replayed, so edges are derived
	// by comparing against the previous tick's record.
	public bool PrimaryFire;
	public bool SecondaryFire;
	public bool Boost;

	// Clamps the axes; call on anything that did not come from this machine.
	public ShipInput Sanitized()
	{
		Pitch = Mathf.Clamp(Pitch, -1.0f, 1.0f);
		Yaw   = Mathf.Clamp(Yaw,   -1.0f, 1.0f);
		Roll  = Mathf.Clamp(Roll,  -1.0f, 1.0f);
		Throttle = Mathf.Clamp(Throttle, -1.0f, 1.0f);
		return this;
	}

	// -1, 0 or +1 from a pair of opposing buttons. Both or neither cancel out.
	public static float Axis(bool positive, bool negative) =>
		(positive ? 1.0f : 0.0f) - (negative ? 1.0f : 0.0f);
}
