using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class PlayerShip : CharacterBody3D, IDamageable
{
	private const float MAX_SPEED = 200.0f;
	private const float MAX_ROLL_SPEED = 2.0f;
	private const float ROLL_ACCELERATION = 2.5f;

	private const float MAX_PITCH_SPEED = 4.0f;
	private const float PITCH_ACCELERATION = 6f;

	private const float MAX_YAW_SPEED = 2.0f;
	private const float YAW_ACCELERATION = 6.0f;

	private const float ACCELERATION = 40.0f;
	private const float MOUSE_SENSITIVITY = 0.18f;

	[ExportGroup("Aim")]
	[Export] public float AimRadius = 100.0f;
	[Export] public float AimDeadzone = 0.05f;
	[Export] public float AimSensitivity = 1.2f;
	[Export] public bool AutoCenterCursor = true;
	[Export] public float AutoCenterDelay = 0f;
	[Export] public float AutoCenterSpeed = 8.0f;
	[ExportSubgroup("Dust Effects")]
	[Export] public float DustAlignSmoothing = 60.0f;
	[Export] public float DustSpawnStartSpeed = 20.0f;
	[Export] public float DustSpawnFullSpeed = 120.0f;
	[Export] public float DustMaxAmountRatio = 1.0f;

	[ExportGroup("Collision")]
	[Export] public float CollisionLinearDamping = 0.9f;
	[Export] public float CollisionAngularDamping = 0.75f;
	[Export] public float CollisionPushOutDistance = 0.1f;
	[Export] public float CollisionDamageSpeedThreshold = 50.0f;
	[Export] public float CollisionDamageMultiplier = 1.0f;

	private float aimTargetYaw = 0.0f;
	private float aimTargetPitch = 0.0f;
	private float aimYaw = 0.0f;
	private float aimPitch = 0.0f;
	private const float AIM_RESPONSIVENESS = 18.0f;

	private Vector2 widgetCursor = Vector2.Zero;
	private bool widgetCursorInitialized = false;
	private double timeSinceLastMouseInput = 0.0;
	public Vector2 GetWidgetCursorPos() => widgetCursor;
	public bool IsViewTurned => _cockpitCamera is FreeLook look && look.IsTurned;

	private Vector3 angularVelocity = Vector3.Zero;
	private Vector3 thrust = Vector3.Zero;
	private Vector3 torque = Vector3.Zero;

	// This tick's intent; the flight model reads this, never Input directly.
	private ShipInput _input;
	// Last tick's intent, so held-button edges can be derived without Input.
	private ShipInput _prevInput;

	// False for a ship driven by a remote or replayed input record: it is simulated
	// from _input, but never samples this machine's keyboard or mouse.
	public bool IsLocallyControlled { get; set; } = true;

	private bool _justUnpaused = false;
	private float _currentMaxSpeed = MAX_SPEED;
	private float _currentAcceleration = ACCELERATION;
	private float _currentRollAcceleration = ROLL_ACCELERATION;
	private float _currentPitchAcceleration = PITCH_ACCELERATION;
	private float _currentYawAcceleration = YAW_ACCELERATION;
	private float _currentMaxRollSpeed = MAX_ROLL_SPEED;
	private float _currentMaxPitchSpeed = MAX_PITCH_SPEED;
	private float _currentMaxYawSpeed = MAX_YAW_SPEED;

	private Node3D dustParticles;
	private GpuParticles3D dustParticlesGpu;
	private CanvasLayer canvasLayer;
	private CanvasLayer _scoreHud;
	private Camera3D _cockpitCamera;
	private Camera3D _externalCamera;
	private bool _isExternalView = false;
	private CockpitBar _healthBar;
	private CockpitReadout _speedReadout;
	private HealthComponent health;
	private bool _isDead = false;
	private uint _liveCollisionLayer;
	private uint _liveCollisionMask;
	private ColorRect _deathScreen;

	private ShipModel _model;
	private Node3D _headlights;
	private bool _showShip = true;

	public float CurrentHealth => health.CurrentHealth;
	public float MaxHealth => health.MaxHealth;
	public float CurrentSpeed => Velocity.Length();
	public static float BaseMaxSpeed => MAX_SPEED;

	public override void _Ready()
	{
		dustParticles = GetNode<Node3D>("dustParticles");
		dustParticlesGpu = dustParticles.GetNodeOrNull<GpuParticles3D>("GPUParticles3D");
		_cockpitCamera = GetNode<Camera3D>("ShakeableCamera");
		_externalCamera = GetNode<Camera3D>("ExternalCamera");
		LoadControlSettings();
		if (dustParticlesGpu != null)
		{
			dustParticlesGpu.Emitting = false;
			dustParticlesGpu.AmountRatio = 0.0f;
		}

		canvasLayer = GetNode<CanvasLayer>("HUD");
		_scoreHud = GetParent().GetParent().GetNodeOrNull<CanvasLayer>("ScoreHUD");
		_healthBar = GetNodeOrNull<CockpitBar>("HealthBar");
		_speedReadout = GetNodeOrNull<CockpitReadout>("SpeedReadout");
		health = GetNode<HealthComponent>("HealthComponent");
		health.HealthChanged += OnHealthChanged;
		health.Died += OnDied;
		_healthBar?.SetFraction(health.CurrentHealth / health.MaxHealth);

		_model = GetNodeOrNull<ShipModel>("Model");
		_headlights = GetNodeOrNull<Node3D>("Headlights");
		// A stand-in has no pilot to see it.
		if (!IsLocallyControlled) _model?.SetInteriorVisible(false);
		var cfg = GetTree().Root.GetNodeOrNull<ConfigFileHandler>("/root/ConfigFileHandler");
		if (cfg != null)
		{
			// Photo mode clears this machine's own view; other players' ships stay visible.
			_showShip = !IsLocallyControlled || cfg.GetShowShipModel();
			_cockpitCamera.Fov = cfg.GetCockpitFov();
			if (_model != null) _model.Visible = _showShip;

			foreach (string nodeName in new[]
			{
				"HealthBar", "SpeedReadout", "AmmoReadout",
				"MissilesReadout", "TargetNameReadout", "TargetDistReadout", "TargetHealthBar", "Radar"
			})
				GetNodeOrNull<Node3D>(nodeName)?.SetVisible(_showShip);

			if (!_showShip)
			{
				canvasLayer.Visible = false;
				if (_scoreHud != null) _scoreHud.Visible = false;
			}
		}

		InitWeapons();
		InitBoost();
		InitTargeting();
		_liveCollisionLayer = CollisionLayer;
		_liveCollisionMask = CollisionMask;

		_deathScreen = GetNodeOrNull<ColorRect>("DeathScreenLayer/DeathScreen");
		// Wired once: hooking this up inside OnDied stacked a new handler on every death.
		if (_deathScreen != null)
			_deathScreen.GetNode<Button>("VBoxContainer/MainMenuButton").Pressed
				+= () => MatchManager.Instance.LeaveMatch();

		// A stand-in for somebody else's ship must not take over the viewport.
		if (IsLocallyControlled) _cockpitCamera.MakeCurrent();

		ApplyActivation();
	}

	private void LoadControlSettings()
	{
		if (ConfigFileHandler.Instance == null)
			return;

		var settings = ConfigFileHandler.Instance.LoadControlSettings();
		if (settings.ContainsKey("aim_sensitivity"))
			AimSensitivity = settings["aim_sensitivity"].AsSingle();
		if (settings.ContainsKey("aim_deadzone"))
			AimDeadzone = settings["aim_deadzone"].AsSingle();
		if (settings.ContainsKey("auto_center_speed"))
			AutoCenterSpeed = settings["auto_center_speed"].AsSingle();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationUnpaused)
			_justUnpaused = true;
	}

	public void TakeDamage(float amount, CollisionShape3D hitShape = null, Node3D source = null)
	{
		health.TakeDamage(amount, source);
	}

	private void OnDied()
	{
		if (_isDead) return;
		_isDead = true;

		EventBus.EmitKilled(Participants.IdOf(this), Participants.IdOf(health.LastAttacker));
		ClearTarget();
		foreach (GatlingWeapon gun in _gatlings)
			gun.Silence();
		Explosion.SpawnAt(this, GlobalPosition);
		ApplyActivation();

		// Only for the ship this machine flies: without the guard, killing somebody released
		// the killer's own cursor, because the victim's stand-in runs this too.
		if (IsLocallyControlled)
		{
			Input.MouseMode = Input.MouseModeEnum.Visible;
		}

		// Only a finished single run gets this ship's own screen; a finished match already has
		// the scoreboard up.
		if (MatchManager.Instance.OnParticipantDestroyed(Participants.IdOf(this)) == DeathOutcome.RunOver)
			ShowDeathScreen();
	}

	// Called by MatchManager. Explicit and network-free: the manager decides when a ship
	// dies for good and when it comes back.
	public void Respawn(Transform3D at)
	{
		RespawnCount++;
		GlobalTransform = at;
		Velocity = Vector3.Zero;
		angularVelocity = Vector3.Zero;
		thrust = Vector3.Zero;
		torque = Vector3.Zero;

		_isDead = false;
		health.Reset();
		ApplyActivation();

		if (!IsLocallyControlled) return;

		if (_deathScreen != null) _deathScreen.Visible = false;
		if (!GetTree().Paused) Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	// Every machine counts the same respawns, so ship state sent before the latest one can be
	// told apart from state sent after it.
	public int RespawnCount { get; private set; }

	// Engine state the thruster flames run on. Read from the ship this machine flies and
	// written onto a stand-in, so other players see the same engines burning.
	public bool ThrustingForward => _input.ThrustForward;
	public bool ThrustingBackward => _input.ThrustBackward;
	public bool ThrusterBoostOn => _isBoosting;

	public void SetRemoteEngines(Vector3 velocity, bool forward, bool backward, bool boosting)
	{
		Velocity = velocity;
		_input.ThrustForward = forward;
		_input.ThrustBackward = backward;
		_isBoosting = boosting;
	}

	// Read from the ship this machine flies and written onto a stand-in, like the engine state.
	public bool HeadlightsOn => _headlights != null && _headlights.Visible;

	public void SetHeadlights(bool on)
	{
		if (_headlights != null) _headlights.Visible = on;
	}

	public void ShowDeathScreen()
	{
		if (_deathScreen != null) _deathScreen.Visible = true;
	}

	// Everything that follows from the two facts about this ship: whether this machine flies
	// it, and whether it is dead. A wreck is invisible and intangible rather than freed, so
	// the same node can fly again.
	private void ApplyActivation()
	{
		bool piloted = IsLocallyControlled && !_isDead;
		SetProcess(!_isDead);
		SetPhysicsProcess(piloted);
		SetProcessInput(piloted);

		Visible = !_isDead;
		CollisionLayer = _isDead ? 0 : _liveCollisionLayer;
		CollisionMask = _isDead ? 0 : _liveCollisionMask;

		if (_isDead && dustParticlesGpu != null)
		{
			dustParticlesGpu.Emitting = false;
			dustParticlesGpu.AmountRatio = 0.0f;
		}
	}

	private void OnHealthChanged(float current, float max)
	{
		_healthBar?.SetFraction(current / max);
	}

	public override void _Input(InputEvent @event)
	{
		// While looking around, the mouse turns the view, not the ship.
		if (@event is InputEventMouseMotion mouseEvent && !Input.IsActionPressed("free_look"))
		{
			if (!widgetCursorInitialized)
			{
				Vector2 sz = GetViewport().GetVisibleRect().Size;
				widgetCursor = sz / 2.0f;
				widgetCursorInitialized = true;
			}

			widgetCursor += mouseEvent.Relative;
			Vector2 center = GetViewport().GetVisibleRect().Size / 2.0f;
			Vector2 offset = widgetCursor - center;
			if (offset.Length() > AimRadius)
				widgetCursor = center + offset.Normalized() * AimRadius;

			timeSinceLastMouseInput = 0.0;
		}
	}

	public override void _Process(double delta)
	{
		// Engines are what other players see of this ship, so they run on stand-ins too.
		UpdateThrusterFlame();

		// Everything below is for the pilot alone: dust is a cockpit effect, the rest is HUD.
		if (!IsLocallyControlled) return;

		UpdateDustSpawnBySpeed();
		AlignDustSpawnToVelocity((float)delta);
		UpdateAutoCenterCursor((float)delta);
		if (_speedReadout != null)
			_speedReadout.Value = $"{CurrentSpeed:F0}";
		UpdateTargetHUD((float)delta);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (IsLocallyControlled)
			_input = SampleLocalInput((float)delta);

		if (CanAct())
		{
			ProcessIntent((float)delta);
			if (IsLocallyControlled) UpdateLocalOnly();
		}

		ProcessBoost((float)delta);
		SimulateTick((float)delta);
		UpdateGimbalTracking((float)delta);

		_prevInput = _input;
	}

	// Turns this ship into a stand-in for a peer: no input, no flight model, and none of
	// the local-player fittings. Must run before the ship enters the tree — _Ready reads
	// this to decide the camera and to apply activation.
	// The sync manager drives the transform; this method itself knows nothing about the net.
	public void MakeRemote()
	{
		IsLocallyControlled = false;
		StripLocalOnlyNodes(this);
	}

	private static void StripLocalOnlyNodes(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			switch (child)
			{
				case Camera3D camera: camera.Current = false; break;
				case CanvasLayer layer: layer.Visible = false; break;
				case Label3D label: label.Visible = false; break;
				case CockpitBar bar: bar.Visible = false; break;
				case CockpitRadar radar: radar.Visible = false; break;
			}
			StripLocalOnlyNodes(child);
		}
	}

	// Dead ships act on nothing, and the first tick after unpausing is skipped so a
	// key held from before the pause does not re-trigger. Clears _justUnpaused.
	private bool CanAct()
	{
		if (_isDead) return false;
		if (_justUnpaused)
		{
			_justUnpaused = false;
			return false;
		}
		return true;
	}

	// Acts on this tick's intent. Edges come from _prevInput, not Input, so a replayed
	// or remote tick fires the same way the tick that produced the record did.
	private void ProcessIntent(float delta)
	{
		timeSinceLastShot += delta;
		_timeSinceLastMissile += delta;

		if (_input.PrimaryFire && timeSinceLastShot >= fireCooldown)
		{
			Shoot();
			timeSinceLastShot = 0.0;
		}

		if (!_input.PrimaryFire && _prevInput.PrimaryFire)
			foreach (GatlingWeapon gun in _gatlings)
				gun.StopFiring();

		if (_input.Boost && !_prevInput.Boost && CanBoost && !_isBoosting)
			ActivateBoost();

		if (_input.SecondaryFire && !_prevInput.SecondaryFire)
			FireMissile();
	}

	// Cosmetic and UI actions outside the intent record, so they read Input directly. Other
	// players see the headlights through the ship state.
	private void UpdateLocalOnly()
	{
		if (Input.IsActionJustPressed("light"))
			SetHeadlights(!HeadlightsOn);

		if (Input.IsActionJustPressed("camera_switch"))
			ToggleCameraView();

		if (Input.IsActionJustPressed("target_cycle"))
			CycleTarget();
	}

	// The flight model for one tick. Reads only _input, so replaying a record reproduces
	// the tick that generated it.
	private void SimulateTick(float delta)
	{
		ReadThrustAndTorqueInput();
		ApplyStopKey(delta);
		IntegrateVelocities(delta);
		MoveAndResolveCollision(delta);
		ApplyRotation(delta);
	}

	// Returns the aim cursor's position in screen space, clamped to the aim circle.
	private Vector2 ResolveAimCursor(Vector2 center)
	{
		if (Input.MouseMode == Input.MouseModeEnum.Visible)
		{
			widgetCursor = GetViewport().GetMousePosition();
			widgetCursorInitialized = true;
			return widgetCursor;
		}

		if (!widgetCursorInitialized)
		{
			widgetCursor = center;
			widgetCursorInitialized = true;
		}
		return widgetCursor;
	}

	// Reads this machine's keyboard and mouse into one tick of intent. A replayed or
	// remote tick assigns _input from a record instead and never calls this.
	private ShipInput SampleLocalInput(float delta)
	{
		ShipInput input = default;

		input.ThrustForward  = Input.IsActionPressed("thrust_forward");
		input.ThrustBackward = Input.IsActionPressed("thrust_backward");
		input.StrafeUp       = Input.IsActionPressed("strafe_up");
		input.StrafeDown     = Input.IsActionPressed("strafe_down");
		input.StrafeRight    = Input.IsActionPressed("strafe_right");
		input.StrafeLeft     = Input.IsActionPressed("strafe_left");
		input.Stop           = Input.IsActionPressed("stop");
		input.Roll           = Input.GetActionStrength("roll_right") - Input.GetActionStrength("roll_left");

		input.PrimaryFire    = Input.IsActionPressed("primary_fire");
		input.SecondaryFire  = Input.IsActionPressed("secondary_fire");
		input.Boost          = Input.IsActionPressed("boost");

		UpdateAim(delta, ref input);
		return input;
	}

	// Turns the aim cursor's offset from screen centre into pitch and yaw input.
	private void UpdateAim(float delta, ref ShipInput input)
	{
		Vector2 center = GetViewport().GetVisibleRect().Size / 2.0f;

		// Offset from centre, scaled to the aim circle and clamped to its edge.
		Vector2 norm = (ResolveAimCursor(center) - center) / AimRadius;
		float mag = norm.Length();
		if (mag > 1.0f)
		{
			norm /= mag;
			mag = 1.0f;
		}

		if (mag < AimDeadzone)
		{
			aimTargetYaw = 0.0f;
			aimTargetPitch = 0.0f;
		}
		else
		{
			aimTargetYaw = -Mathf.Clamp(norm.X, -1.0f, 1.0f) * MOUSE_SENSITIVITY * AimSensitivity;
			aimTargetPitch = Mathf.Clamp(-norm.Y, -1.0f, 1.0f) * MOUSE_SENSITIVITY * AimSensitivity;
		}

		aimYaw = Mathf.Lerp(aimYaw, aimTargetYaw, AIM_RESPONSIVENESS * delta);
		aimPitch = Mathf.Lerp(aimPitch, aimTargetPitch, AIM_RESPONSIVENESS * delta);

		// Only the result is intent; sensitivity and smoothing stay local.
		input.Pitch = aimPitch;
		input.Yaw = aimYaw;
	}

	// Collects the movement keys into current frame's thrust and torque vectors.
	private void ReadThrustAndTorqueInput()
	{
		_input = _input.Sanitized();
		thrust = Vector3.Zero;

		// Forward axis (-Z): boost forces full forward thrust and disables backward.
		if (_isBoosting)
		{
			thrust -= Transform.Basis.Z * _currentAcceleration;
		}
		else
		{
			thrust -= Transform.Basis.Z * _currentAcceleration
				* ShipInput.Axis(_input.ThrustForward, _input.ThrustBackward);
		}

		thrust += Transform.Basis.Y * _currentAcceleration * ShipInput.Axis(_input.StrafeUp, _input.StrafeDown);
		thrust += Transform.Basis.X * _currentAcceleration * ShipInput.Axis(_input.StrafeRight, _input.StrafeLeft);

		torque = new Vector3(
			_input.Pitch * _currentPitchAcceleration,
			_input.Yaw * _currentYawAcceleration,
			-_input.Roll * _currentRollAcceleration);
	}

	private void ApplyStopKey(float delta)
	{
		if (!_input.Stop || _isBoosting)
			return;

		thrust = Vector3.Zero;
		torque = Vector3.Zero;
		if (Velocity.Length() > 0.1f)
			Velocity = Velocity.MoveToward(Vector3.Zero, ACCELERATION * delta);
		angularVelocity = angularVelocity.MoveToward(Vector3.Zero, MAX_PITCH_SPEED * 4f * delta);
	}

	// Integrates thrust and torque, caps both against the ship's current limits.
	private void IntegrateVelocities(float delta)
	{
		Velocity += thrust * delta;

		if (_isBoostDecaying)
		{
			// While boost bleeds off, the cap eases from the boosted speed back to normal.
			float decayProgress = 1.0f - (_boostDecayTimer / BoostDecayTime);
			Velocity = Velocity.LimitLength(Mathf.Lerp(_speedAtBoostEnd, MAX_SPEED, decayProgress));
		}
		else
		{
			Velocity = Velocity.LimitLength(_currentMaxSpeed);
		}

		angularVelocity += torque * delta;
		angularVelocity.X = Mathf.Clamp(angularVelocity.X, -_currentMaxPitchSpeed, _currentMaxPitchSpeed);
		angularVelocity.Y = Mathf.Clamp(angularVelocity.Y, -_currentMaxYawSpeed, _currentMaxYawSpeed);
		angularVelocity.Z = Mathf.Clamp(angularVelocity.Z, -_currentMaxRollSpeed, _currentMaxRollSpeed);
	}

	private void MoveAndResolveCollision(float delta)
	{
		var collision = MoveAndCollide(Velocity * delta);
		if (collision == null)
			return;

		Vector3 normal = collision.GetNormal();

		float impactSpeed = Mathf.Max(0.0f, -Velocity.Dot(normal));
		if (impactSpeed > CollisionDamageSpeedThreshold)
			DamageManager.Instance.Report(this,
				(impactSpeed - CollisionDamageSpeedThreshold) * CollisionDamageMultiplier,
				null, collision.GetCollider() as Node3D);

		Velocity = Velocity.Slide(normal) * CollisionLinearDamping;
		angularVelocity *= CollisionAngularDamping;

		if (CollisionPushOutDistance > 0.0f)
			GlobalPosition += normal * CollisionPushOutDistance;
	}

	private void ApplyRotation(float delta)
	{
		RotateObjectLocal(Vector3.Right, angularVelocity.X * delta);
		RotateObjectLocal(Vector3.Up, angularVelocity.Y * delta);
		RotateObjectLocal(Vector3.Back, angularVelocity.Z * delta);
		Transform = Transform.Orthonormalized();
	}

	private void ToggleCameraView()
	{
		_isExternalView = !_isExternalView;

		bool cockpit = !_isExternalView;
		(cockpit ? _cockpitCamera : _externalCamera).MakeCurrent();
		canvasLayer.Visible = cockpit && _showShip;
		if (_scoreHud != null) _scoreHud.Visible = cockpit && _showShip;
		if (dustParticles != null) dustParticles.Visible = cockpit;
	}

	private void UpdateAutoCenterCursor(float delta)
	{
		if (!AutoCenterCursor || !widgetCursorInitialized)
			return;

		timeSinceLastMouseInput += delta;
		if (timeSinceLastMouseInput >= AutoCenterDelay)
		{
			Vector2 center = GetViewport().GetVisibleRect().Size / 2.0f;
			float blendAmount = Mathf.Clamp(delta * AutoCenterSpeed, 0.0f, 1.0f);
			widgetCursor = widgetCursor.Lerp(center, blendAmount);
		}
	}

	private void UpdateDustSpawnBySpeed()
	{
		if (dustParticlesGpu == null)
			return;

		float speed = Velocity.Length();
		if (speed < DustSpawnStartSpeed)
		{
			dustParticlesGpu.Emitting = false;
			dustParticlesGpu.AmountRatio = 0.0f;
			return;
		}

		dustParticlesGpu.Emitting = true;
		float speedRange = Mathf.Max(0.01f, DustSpawnFullSpeed - DustSpawnStartSpeed);
		float t = Mathf.Clamp((speed - DustSpawnStartSpeed) / speedRange, 0.0f, 1.0f);
		dustParticlesGpu.AmountRatio = Mathf.Clamp(t * DustMaxAmountRatio, 0.0f, 1.0f);
	}

	private void AlignDustSpawnToVelocity(float delta)
	{
		if (dustParticles == null)
			return;

		if (Velocity.Length() < DustSpawnStartSpeed)
			return;

		Vector3 desiredAxis = Velocity.Normalized();
		Vector3 reference = dustParticles.GlobalTransform.Basis.Z.Normalized();
		if (Mathf.Abs(reference.Dot(desiredAxis)) > 0.98f)
			reference = dustParticles.GlobalTransform.Basis.X.Normalized();

		Vector3 right = reference.Cross(desiredAxis).Normalized();
		Vector3 forward = desiredAxis.Cross(right).Normalized();
		Basis targetBasis = new Basis(right, desiredAxis, forward);

		Quaternion currentRotation = dustParticles.GlobalTransform.Basis.GetRotationQuaternion();
		Quaternion targetRotation = targetBasis.GetRotationQuaternion();
		float blend = Mathf.Clamp(delta * DustAlignSmoothing, 0.0f, 1.0f);
		Quaternion blendedRotation = currentRotation.Slerp(targetRotation, blend);

		dustParticles.GlobalTransform = new Transform3D(new Basis(blendedRotation), dustParticles.GlobalTransform.Origin);
	}

	private void UpdateThrusterFlame()
	{
		if (_model == null) return;

		float forward;
		if (_isBoosting)
			forward = 1.5f;
		else if (_input.ThrustForward)
			forward = 0.6f + _currentBoostPower * 0.4f;
		else if (Velocity.LengthSquared() > 1f)
			forward = 0.15f;
		else
			forward = 0.0f;

		float reverse = _input.ThrustBackward && !_isBoosting ? 0.6f : 0.0f;
		_model.SetThrottle(forward, reverse);
	}
}
