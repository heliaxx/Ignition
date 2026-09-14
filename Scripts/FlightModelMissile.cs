using Godot;
using System.Collections.Generic;

// Physics-based guided missile: 6-DOF thrust flight with RCS thruster visuals
// and two guidance modes (Pursuit / Proportional Navigation). Deals damage to
// the IDamageable it hits, with a proximity fuse and an arming delay.
public partial class FlightModelMissile : RigidBody3D
{
	[ExportGroup("Debug")]
	[Export] public bool ShowAllThrusters = false;
	[Export] public bool UnlimitedFuel = false;
	[Export] public bool DisableStrafe = false;
	[Export] public bool EnableRearThrust = false;

	public enum ChaseModes
	{
		Pursuit,                // goes after the target's current position
		ProportionalNavigation, // constant-bearing intercept; falls back to Pursuit when not facing the target
	}

	[ExportGroup("Chase Modes")]
	[Export] public ChaseModes ChaseMode = ChaseModes.ProportionalNavigation;
	[Export] public float PnGain = 4.0f; // aggressivity of PN corrections

	[ExportGroup("Missile specifications")]
	[Export] public float LaunchSpeed = 150.0f;      // speed when launched from ship
	[Export] public bool InheritLauncherVelocity = true; // add the launcher's velocity on top of LaunchSpeed
	[Export] public float LinearAcceleration = 150.0f;   // max sideways acceleration
	[Export] public float ForwardAcceleration = 250.0f; // max forward acceleration
	[Export] public float AngularAgility = Mathf.Pi; // turning torque (angular acceleration, rad/s^2)
	[Export] public bool ExplodeOnFuelLoss = false;
	[Export] public float Damage = 60.0f; // dealt to an IDamageable on impact
	[Export] public float ArmingTime = 0.2f; // ignore impacts for this duration after launch (seconds)
	[Export] public float ProximityRadius = 10.0f; // detonate when within this distance of the target (0 = contact only)
	[Export] public float Lifetime = 15.0f; // seconds before the missile despawns if it never hits anything

	[ExportGroup("Visual Parameters")]
	[Export] public bool HideRcs = false;

	[ExportGroup("Target")]
	[Export] public Node3D Target;

	// Launcher velocity applied at launch when InheritLauncherVelocity is on, so the
	// missile leaves the ship already matching its momentum. Set before adding to the tree.
	public Vector3 InheritedVelocity { get; set; } = Vector3.Zero;

	// Ship that launched this missile for kill credit. Set before adding to the tree.
	public Node3D Source { get; set; }

	// Impact point. A replicated copy has to blow up when the original does, not three
	// seconds later when the coasting wreck is finally freed.
	[Signal] public delegate void DetonatedEventHandler(Vector3 at);

	// Remaining engine burn time in seconds; with the fuel spent the missile can only coast.
	private float _thrustTime = 10.0f;

	// How long a spent missile stays in the tree: its impact effects and the smoke it already
	// laid (3.3 s) are its children and would vanish with it.
	private const float LingerTime = 3.5f;

	private MissileThruster _thrustForward;
	private MissileThruster _thrustRight;
	private MissileThruster _thrustLeft;
	private MissileThruster _thrustUp;
	private MissileThruster _thrustDown;
	private MissileThruster _thrustLeftAft;
	private MissileThruster _thrustRightAft;
	private MissileThruster _thrustUpAft;
	private MissileThruster _thrustDownAft;
	private readonly List<MissileThruster> _allThrusters = new();

	private ShapeCast3D _collisionCheck;

	private Vector3 _targetPosition = Vector3.Zero;
	private Vector3 _targetVelocity = Vector3.Zero;
	private bool _isFacingTarget = false;
	private bool _noTarget = true;
	private bool _hitTarget = false;
	private float _armedTimer = 0f;
	private float _lifeTimer = 0f;
	private float _despawnAt = 0f; // set on detonation: when the explosion effects are done

	public override void _Ready()
	{
		_thrustForward = GetNode<MissileThruster>("ThrustEffect");
		_thrustDown = GetNode<MissileThruster>("ThrustEffect2");
		_thrustUp = GetNode<MissileThruster>("ThrustEffect5");
		_thrustUpAft = GetNode<MissileThruster>("ThrustEffect4");
		_thrustDownAft = GetNode<MissileThruster>("ThrustEffect3");
		_thrustRight = GetNode<MissileThruster>("ThrustEffect6");
		_thrustLeft = GetNode<MissileThruster>("ThrustEffect7");
		_thrustLeftAft = GetNode<MissileThruster>("ThrustEffect8");
		_thrustRightAft = GetNode<MissileThruster>("ThrustEffect9");
		_collisionCheck = GetNode<ShapeCast3D>("impactcheck");

		_allThrusters.Add(_thrustForward);
		_allThrusters.Add(_thrustDown);
		_allThrusters.Add(_thrustDownAft);
		_allThrusters.Add(_thrustLeft);
		_allThrusters.Add(_thrustLeftAft);
		_allThrusters.Add(_thrustRight);
		_allThrusters.Add(_thrustRightAft);
		_allThrusters.Add(_thrustUp);
		_allThrusters.Add(_thrustUpAft);

		foreach (MissileThruster t in _allThrusters)
		{
			if (t.AnimationStart() != Error.Ok)
				GD.PushError(this, " failed to start thruster animation on ", t);
			if (!ShowAllThrusters)
				t.Hide();
		}

		// Launch along the missile's own forward (-Z), the same convention the
		// guidance and thrust use.
		LinearVelocity = -GlobalTransform.Basis.Z * LaunchSpeed
			+ (InheritLauncherVelocity ? InheritedVelocity : Vector3.Zero);

		// Impact detection: the RigidBody's own contact reporting plus the
		// impactcheck ShapeCast, whose inline script emits body_hit with the collider.
		ContactMonitor = true;
		MaxContactsReported = 4;
		BodyShapeEntered += OnBodyShapeEntered;
		_collisionCheck.Connect("body_hit", Callable.From<Node3D>(OnImpactcheckBodyHit));

		// Exclude the launcher from both the impact sweep and physical contacts,
		// so the missile never collides with the ship that fired it.
		if (_ignoredBody != null && IsInstanceValid(_ignoredBody))
		{
			_collisionCheck.AddException(_ignoredBody);
			AddCollisionExceptionWith(_ignoredBody);
		}

		_smokeTrail = GetNodeOrNull<GpuParticles3D>("SmokeTrail");
	}

	private GpuParticles3D _smokeTrail;

	private CollisionObject3D _ignoredBody;

	// Call before adding to the tree: the missile will never collide with / detonate
	// on this body (the launcher). Prevents self-detonation on spawn.
	public void IgnoreBody(CollisionObject3D body) => _ignoredBody = body;

	// Turns this into a copy of a missile somebody else fired: no physics, no guidance, no
	// collisions, so it can neither steer nor detonate. MissileSync drives its transform.
	// Call after it enters the tree — _Ready resolves the collision check this switches off.
	public void MakeRemote()
	{
		Freeze = true;
		SetPhysicsProcess(false);
		CollisionLayer = 0;
		CollisionMask = 0;
		if (_collisionCheck != null) _collisionCheck.Enabled = false;
	}

	// Ends a copy: gone at once, but kept until its trail has faded. A copy runs no physics
	// tick, so a timer frees it.
	public void Vanish()
	{
		HideBody();
		GetTree().CreateTimer(LingerTime).Timeout += () =>
		{
			if (IsInstanceValid(this)) QueueFree();
		};
	}

	private void SetSmokeTrail(bool emitting)
	{
		if (_smokeTrail != null)
			_smokeTrail.Emitting = emitting;
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		_armedTimer += dt;
		_lifeTimer += dt;
		_thrustTime -= dt;

		// Already detonated: coast while the explosion effects play
		// out, then free the missile.
		if (_hitTarget)
		{
			if (_lifeTimer >= _despawnAt)
				QueueFree();
			return;
		}

		// End of life without ever hitting anything: despawn.
		if (_lifeTimer >= Lifetime)
		{
			QueueFree();
			return;
		}

		// Out of fuel: coast until the lifetime runs out.
		if (_thrustTime <= 0 && !UnlimitedFuel)
		{
			foreach (MissileThruster t in _allThrusters)
			{
				t.Shutdown();
				t.Hide();
			}
			SetSmokeTrail(false); // engine off, no more smoke
			if (ExplodeOnFuelLoss)
				MissileImpact(null);
			return;
		}

		ComputeTargetPosition(dt);

		// Proximity fuse: detonate once we're within ProximityRadius of the target,
		// damaging the target's owner (the GimbalTarget marker isn't damageable).
		if (_armedTimer >= ArmingTime && ProximityRadius > 0f && TargetValid
			&& GlobalPosition.DistanceTo(_targetPosition) < ProximityRadius)
		{
			Node3D fuseCollider = (Target as GimbalTarget)?.TargetOwner ?? Target;
			MissileImpact(fuseCollider);
			return;
		}

		// With no valid target, coast straight ahead under forward thrust instead of
		// steering toward the stale _targetPosition (which would home to world origin).
		if (_noTarget)
			MissileThrust(Vector3.Zero, true, true);
		else
		{
			ComputeThrust();
			ComputeRotationThrust();
		}

		// Sweep the impactcheck along this frame's travel; TargetPosition is in the
		// shapecast's local space, so the world-space velocity is rotated by basis only.
		_collisionCheck.TargetPosition = _collisionCheck.GlobalBasis.Inverse() * LinearVelocity * dt;
	}

	private void ComputeThrust()
	{
		switch (ChaseMode)
		{
			case ChaseModes.Pursuit:
				ComputeStrafeThrust();
				break;
			case ChaseModes.ProportionalNavigation:
				if (_isFacingTarget)
					ComputePnThrust();
				else
					ComputeStrafeThrust();
				break;
		}
	}

	private void ComputeStrafeThrust()
	{
		Vector3 positionError = GetPositionError();
		Vector3 localError = Basis.Inverse() * positionError;
		Vector3 finalThrust = Vector3.Zero;

		for (int axis = 0; axis < 3; axis++)
		{
			float distanceToStop = (LinearVelocity[axis] * LinearVelocity[axis]) / (2.0f * LinearAcceleration);
			bool isTowardsTarget = Mathf.Sign(LinearVelocity[axis]) == Mathf.Sign(positionError[axis]);
			if (Mathf.Abs(positionError[axis]) <= distanceToStop && isTowardsTarget)
				finalThrust[axis] = -Mathf.Sign(positionError[axis]);
			else
				finalThrust[axis] = Mathf.Sign(positionError[axis]);
		}

		bool hide = new Vector2(localError.X, localError.Y).Length() < 2.0f;
		MissileThrust(finalThrust, true, hide);
	}


	private void ComputeRotationThrust()
	{
		Vector3 rotationError = GetRotationError();
		float errorAngle = rotationError.Length();

		if (errorAngle < 0.1f)
		{
			Quaternion quat = GlobalTransform.LookingAt(_targetPosition, GlobalBasis.Y)
				.Basis.GetRotationQuaternion();
			MissileForceRotation(quat);
			return;
		}

		Vector3 rotationAxis = rotationError / errorAngle;
		float spinTowardsTarget = AngularVelocity.Dot(rotationAxis);
		float angleToStop = (spinTowardsTarget * spinTowardsTarget) / (2.0f * AngularAgility);

		Vector3 finalTorque;
		if (angleToStop >= errorAngle && spinTowardsTarget > 0.0f)
			finalTorque = -rotationAxis;
		else
			finalTorque = rotationAxis;

		bool hide = finalTorque.Length() < 0.2f;
		MissileRotation(finalTorque, false, false, hide);
	}

	private bool TargetValid => Target != null && IsInstanceValid(Target);

	// Computes _targetPosition; tracks a moving target's velocity when possible.
	private void ComputeTargetPosition(float delta)
	{
		if (!TargetValid)
		{
			_noTarget = true;
			return;
		}
		_noTarget = false;
		_targetPosition = Target.GlobalPosition;
		_targetVelocity = Vector3.Zero;
		if (Target is GimbalTarget gt)
			_targetVelocity = gt.GetVelocity(); // velocity of the marker's parent body
		else if (Target is CharacterBody3D cb)
			_targetVelocity = cb.Velocity;
		else if (Target is RigidBody3D rb)
			_targetVelocity = rb.LinearVelocity;

		float angleTowardsTarget = GetPositionError().Normalized().Dot(LinearVelocity.Normalized());
		_isFacingTarget = angleTowardsTarget > 0.1f;
	}

	// Proportional Navigation: aim point offset from the missile by the commanded
	// acceleration, keeping the bearing to the target constant until impact.
	private Vector3 GetPnAimPoint(Vector3 targetPosition, Vector3 targetVelocity)
	{
		Vector3 los = targetPosition - GlobalPosition;
		float rangeToTarget = los.Length();
		if (rangeToTarget < 0.5f)
			return targetPosition;

		Vector3 relativeVelocity = targetVelocity - LinearVelocity;
		Vector3 losUnit = los / rangeToTarget;
		float closingVelocity = -relativeVelocity.Dot(losUnit);
		Vector3 losRotation = los.Cross(relativeVelocity) / (rangeToTarget * rangeToTarget);
		Vector3 commandedAcceleration = PnGain * closingVelocity * losRotation.Cross(losUnit);
		return GlobalPosition + commandedAcceleration;
	}

	private void ComputePnThrust()
	{
		Vector3 aimPoint = GetPnAimPoint(_targetPosition, _targetVelocity);
		Vector3 pnDirection = (aimPoint - GlobalPosition).LimitLength(LinearAcceleration) / LinearAcceleration;
		bool hide = pnDirection.Length() <= 0.5f;
		MissileThrust(pnDirection, true, hide);
	}

	private Vector3 GetPositionError() => _targetPosition - GlobalPosition;

	private Vector3 GetRotationError()
	{
		if (!TargetValid)
			return Vector3.Zero;
		Vector3 desiredForward = (_targetPosition - GlobalPosition).Normalized();
		Vector3 currentForward = -GlobalTransform.Basis.Z;
		Quaternion rotationDiff = new Quaternion(currentForward, desiredForward);
		return rotationDiff.GetAxis() * rotationDiff.GetAngle();
	}

	// Simulated thrust in the given (world) direction.
	private void MissileThrust(Vector3 direction, bool reset = false, bool hide = false)
	{
		if (reset)
			foreach (MissileThruster t in _allThrusters)
				t.Hide();

		direction = Basis.Inverse() * direction;
		if (DisableStrafe)
			direction = new Vector3(0, 0, direction.Z);
		// A missile never retreats: forward thrust stays at full burn.
		if (!EnableRearThrust)
			direction.Z = -1;

		if (!hide && !HideRcs)
		{
			// right or left
			switch (Mathf.Sign(direction.X))
			{
				case 1: _thrustRight.How(); _thrustRightAft.How(); break;
				case -1: _thrustLeft.How(); _thrustLeftAft.How(); break;
			}
			// up or down
			switch (Mathf.Sign(direction.Y))
			{
				case 1: _thrustUp.How(); _thrustUpAft.How(); break;
				case -1: _thrustDown.How(); _thrustDownAft.How(); break;
			}
		}

		if (!_hitTarget && _thrustTime >= 0)
		{
			switch (Mathf.Sign(direction.Z))
			{
				case 1: _thrustForward.How(); break;
				case -1: _thrustForward.Show(); break;
			}
		}

		ApplyCentralForce(Basis * (direction * Mass * new Vector3(LinearAcceleration, LinearAcceleration, ForwardAcceleration)));
	}

	// Blends the missile's orientation toward the given rotation, bypassing torque.
	private void MissileForceRotation(Quaternion face, float weight = 20.0f)
	{
		Quaternion currentRotation = GlobalBasis.GetRotationQuaternion().Normalized();
		currentRotation = currentRotation.Slerp(face, Mathf.Min(weight * (float)GetPhysicsProcessDeltaTime(), 1.0f));
		Basis = new Basis(currentRotation);
	}

	// Simulated rotation thrust about the given (world) axis; opposing fore/aft
	// RCS thrusters fire as a couple.
	private void MissileRotation(Vector3 direction, bool reset = true, bool visualOnly = false, bool hide = false)
	{
		if (reset)
			foreach (MissileThruster t in _allThrusters)
				t.Hide();

		direction = Basis.Inverse() * direction;
		float temporaryMultiplier = 1.0f;
		if (direction.Length() < Mathf.DegToRad(15))
			temporaryMultiplier = 0.5f;

		if (!hide && !HideRcs)
		{
			switch (Mathf.Sign(direction.X))
			{
				case 1: _thrustUp.How(); _thrustDownAft.How(); _thrustDown.Hide(); break;
				case -1: _thrustDown.How(); _thrustUpAft.How(); _thrustUp.Hide(); break;
			}
			switch (Mathf.Sign(direction.Y))
			{
				case 1: _thrustLeft.How(); _thrustRightAft.How(); _thrustLeft.Hide(); break;
				case -1: _thrustRight.How(); _thrustLeftAft.How(); _thrustLeft.Hide(); break;
			}
		}

		if (visualOnly)
			return;
		ApplyTorque(Basis * direction * AngularAgility * temporaryMultiplier);
	}

	// Detonation: deals damage, plays the explosion effects, and leaves the wreck
	// coasting until despawn.
	private void MissileImpact(Node3D collider, CollisionShape3D hitShape = null)
	{
		if (_hitTarget)
			return;
		// Never detonate on the launcher that fired this missile.
		if (collider != null && collider == _ignoredBody)
			return;
		// Impacts during the arming delay are ignored.
		if (_armedTimer < ArmingTime)
			return;

		// Through the manager so a hit on a ship is the server's call. Asteroids need the hit
		// CollisionShape3D to know which asteroid was struck (see AsteroidBody); they are not
		// replicated, so the manager keeps that on the local path.
		if (collider is IDamageable && collider is Node3D target)
			DamageManager.Instance.Report(target, Damage, hitShape, Source);

		GetNode<AudioStreamPlayer3D>("explosion").Play();
		GetNode<GpuParticles3D>("impact").Emitting = true;
		Explosion.SpawnAt(this, GlobalPosition); // big space explosion at the impact point
		HideBody();
		GetNode<ShapeCast3D>("impactcheck").QueueFree();
		_thrustTime = -1;
		_hitTarget = true;
		_despawnAt = _lifeTimer + LingerTime;
		EmitSignal(SignalName.Detonated, GlobalPosition);
	}

	private void HideBody()
	{
		SetSmokeTrail(false); // already-emitted smoke stays in the world
		foreach (MissileThruster t in _allThrusters)
		{
			t.Shutdown();
			t.Hide();
		}
		GetNode<Node3D>("missile final_001").Hide();
		GetNode<Node3D>("Text_008").Hide();
		GetNodeOrNull<Node3D>("BurnGlow").Hide();
	}

	// Resolves which CollisionShape3D of a StaticBody3D was hit, from a physics
	// shape index. Asteroids route damage by the hit shape's metadata.
	private static CollisionShape3D ResolveHitShape(Node3D collider, int shapeIndex)
	{
		if (collider is not StaticBody3D body)
			return null;
		uint ownerId = body.ShapeFindOwner(shapeIndex);
		return body.ShapeOwnerGetOwner(ownerId) as CollisionShape3D;
	}

	private void OnImpactcheckBodyHit(Node3D collider)
	{
		CollisionShape3D hitShape = null;
		if (_collisionCheck.GetCollisionCount() > 0)
			hitShape = ResolveHitShape(collider, _collisionCheck.GetColliderShape(0));
		MissileImpact(collider, hitShape);
	}

	private void OnBodyShapeEntered(Rid bodyRid, Node body, long bodyShapeIndex, long localShapeIndex)
	{
		var collider = body as Node3D;
		MissileImpact(collider, ResolveHitShape(collider, (int)bodyShapeIndex));
	}
}
