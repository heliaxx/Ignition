using Godot;

// Collision damage from how fast two bodies closed on each other. A ram between two ships
// hurts both equally; anything else — rocks, stations — only hurts the ship that hit it.
public static class Ramming
{
	public static float ClosingSpeed(Vector3 velocity, Node other, Vector3 normal) =>
		Mathf.Max(0.0f, -(velocity - VelocityOf(other)).Dot(normal));

	public static bool IsShip(Node body) => body is PlayerShip or Fighter;

	public static void Report(Node3D rammer, Node3D other, float closingSpeed, float threshold, float multiplier)
	{
		// A missile deals its own impact damage.
		if (other is FlightModelMissile || closingSpeed <= threshold) return;

		float damage = (closingSpeed - threshold) * multiplier;
		if (IsShip(other))
			DamageManager.Instance.ReportRam(rammer, other, damage);
		else
			// Self-inflicted, so whoever last hit the ship keeps the kill rather than the rock.
			DamageManager.Instance.Report(rammer, damage, null, rammer);
	}

	private static Vector3 VelocityOf(Node body) => body switch
	{
		CharacterBody3D character => character.Velocity,
		RigidBody3D rigid => rigid.LinearVelocity,
		_ => Vector3.Zero,
	};
}
