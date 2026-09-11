using Godot;

public partial class AsteroidBody : StaticBody3D, IDamageable
{
	public ChunkedAsteroidField Field { get; set; }

	// Which rock a hit shape belongs to; 0 when the shape carries no asteroid.
	public static ulong IdOf(CollisionShape3D hitShape) =>
		hitShape != null && hitShape.HasMeta("asteroid_id")
			? (ulong)(long)hitShape.GetMeta("asteroid_id")
			: 0UL;

	public void TakeDamage(float amount, CollisionShape3D hitShape = null, Node3D source = null)
	{
		if (Field == null || hitShape == null) return;
		if (!hitShape.HasMeta("asteroid_id")) return;

		ulong asteroidId = IdOf(hitShape);
		if (Field.DamageAsteroid(asteroidId, (int)amount))
			Field.DestroyAsteroid(asteroidId);
	}
}
