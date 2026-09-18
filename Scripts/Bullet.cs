using Godot;

public partial class Bullet : Node3D
{
	public float Speed { get; set; } = 2500f;
	[Export] public float Damage { get; set; } = 10f;
	private const float LIFETIME = 2f;
	private const float COLLISION_DESTROY_DELAY = 1f;

	private Vector3 velocity = Vector3.Zero;
	private bool _hasHit = false;

	// Velocity inherited from the spawner. Set before adding to the scene.
	public Vector3 InheritedVelocity { get; set; } = Vector3.Zero;

	// Ship that fired this bullet for kill credit. Set before adding to the scene.
	public Node3D Source { get; set; }

	// False on a tracer relayed from another peer: it flies and sparks, but only the peer
	// that fired reports the hit, so damage is never counted twice.
	public bool HasAuthority { get; set; } = true;

	private MeshInstance3D mesh;
	private RayCast3D ray;
	private GpuParticles3D particles;

	public override void _Ready()
	{
		mesh = GetNode<MeshInstance3D>("MeshInstance3D");
		ray = GetNode<RayCast3D>("RayCast3D");
		// Swept by hand every tick instead.
		ray.Enabled = false;
		particles = GetNode<GpuParticles3D>("GPUParticles3D");
		velocity = new Vector3(0, 0, -Speed);

		// Automatic self-destruction after lifetime expiry
		GetTree().CreateTimer(LIFETIME).Timeout += QueueFree;
	}

	public override void _PhysicsProcess(double delta)
	{
		// Local velocity (forward) + inherited world velocity from spawner
		Vector3 motion = (Transform.Basis * velocity + InheritedVelocity) * (float)delta;

		if (!_hasHit)
		{
			// The ray spans exactly this tick's travel: a bullet covers more ground per tick than
			// any fixed ray, and a surface in the gap would be stepped over without a hit.
			ray.TargetPosition = Transform.Basis.Inverse() * motion;
			ray.ForceRaycastUpdate();
		}

		if (!_hasHit && ray.IsColliding())
		{
			_hasHit = true;
			// The hit can be most of a tick ahead; the sparks belong where it landed.
			GlobalPosition = ray.GetCollisionPoint();
			mesh.Visible = false;
			particles.Emitting = true;
			velocity = Vector3.Zero;

			var collider = ray.GetCollider();

			if (HasAuthority && collider is IDamageable && collider is Node3D target)
			{
				CollisionShape3D hitShape = null;
				if (collider is StaticBody3D body)
				{
					uint ownerId = body.ShapeFindOwner(ray.GetColliderShape());
					hitShape = body.ShapeOwnerGetOwner(ownerId) as CollisionShape3D;
				}
				DamageManager.Instance.Report(target, Damage, hitShape, Source);
			}
			GetTree().CreateTimer(COLLISION_DESTROY_DELAY).Timeout += QueueFree;
		}
		else
		{
			Position += motion;
		}
	}
}
