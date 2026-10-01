using Godot;

// Flight assist holds the motion the pilot commands and cancels the rest; off, flight is Newtonian.
public partial class PlayerShip
{
	[ExportGroup("Flight Assist")]
	[Export] public bool FlightAssistOnSpawn = false;
	// Share of each axis' angular acceleration the assist may spend holding the commanded rate.
	[Export] public float AssistRotationAuthority = 1.0f;
	// Share of the thrusters the assist spends following the pilot; Elite measures about 0.75.
	[Export] public float AssistThrustRatio = 0.75f;
	// How much harder than full thrust the assist sheds velocity the pilot never asked for; Elite: ~3x.
	[Export] public float UnintendedBrakeMultiplier = 3.0f;
	// How far the throttle lever travels per second in throttle mode while W or S is held.
	[Export] public float ThrottleLeverRate = 0.75f;

	// The toggle switches between full assist and a second setting: off, or partial.
	private bool _flightAssist;
	private bool _togglePartial = false;
	private bool _throttleMode = false;
	private float _throttleLever = 0.0f;
	private string _speedBarTitle = "";

	// Local velocity the pilot built up under assist. The rest — left over from a turn, from
	// pushing against the motion, or from flying unassisted — is unintended.
	private Vector3 _intendedVelocity;

	// What the main engine is actually doing, which with assist on differs from the keys:
	// the ship brakes on its own.
	private bool _engineForward;
	private bool _engineBackward;

	private void InitFlightAssist()
	{
		_flightAssist = FlightAssistOnSpawn;
	}

	private void SampleFlightAssist(float delta, ref ShipInput input)
	{
		if (Input.IsActionJustPressed("flight_assist"))
			_flightAssist = !_flightAssist;

		float axis = ShipInput.Axis(input.ThrustForward, input.ThrustBackward);
		if (_throttleMode)
		{
			if (input.Stop)
				_throttleLever = 0.0f;
			else
				_throttleLever = Mathf.Clamp(_throttleLever + axis * ThrottleLeverRate * delta, -1.0f, 1.0f);
		}
		else
		{
			_throttleLever = axis;
		}

		WriteAssist(ref input);
	}

	// Hands off the controls: in throttle mode the lever stays where it was left.
	private ShipInput HandsOffInput()
	{
		if (!_throttleMode) _throttleLever = 0.0f;
		ShipInput input = default;
		WriteAssist(ref input);
		return input;
	}

	private void WriteAssist(ref ShipInput input)
	{
		input.FlightAssist = _flightAssist || _togglePartial;
		input.Partial = !_flightAssist && _togglePartial;
		input.Throttle = _throttleLever;
	}

	// Steers velocity and angular velocity toward what the pilot commands, spending no more
	// than the thrusters could deliver this tick. Partial, velocity is left to plain thrust.
	private void ApplyFlightAssist(float delta)
	{
		if (_input.Partial)
		{
			Velocity += thrust * delta;
			_intendedVelocity = Vector3.Zero;
		}
		else
			HoldLocalVelocity(CommandedLocalVelocity(), delta);

		HoldCommandedRotation(delta);
	}

	// The stop key: full thrust brakes the ship to a standstill whatever the assist setting,
	// and the rotation hold still lets the pilot turn.
	private void ApplyStop(float delta)
	{
		Vector3 local = Transform.Basis.Transposed() * Velocity;
		SetLocalVelocity(local, new Vector3(
			Mathf.MoveToward(local.X, 0.0f, _currentStrafeAcceleration * delta),
			Mathf.MoveToward(local.Y, 0.0f, _currentStrafeAcceleration * delta),
			Mathf.MoveToward(local.Z, 0.0f, (local.Z < 0.0f ? _currentReverseAcceleration : _currentAcceleration) * delta)));
		HoldCommandedRotation(delta);
	}

	// Rate command: the aim offset asks for a turn rate, and centring it stops the turn.
	private void HoldCommandedRotation(float delta)
	{
		Vector3 rateTarget = new Vector3(
			AimDeflection(_input.Pitch) * _currentMaxPitchSpeed,
			AimDeflection(_input.Yaw) * _currentMaxYawSpeed,
			-_input.Roll * _currentMaxRollSpeed);

		float authority = AssistRotationAuthority * delta;
		angularVelocity = new Vector3(
			Mathf.MoveToward(angularVelocity.X, rateTarget.X, _currentPitchAcceleration * authority),
			Mathf.MoveToward(angularVelocity.Y, rateTarget.Y, _currentYawAcceleration * authority),
			Mathf.MoveToward(angularVelocity.Z, rateTarget.Z, _currentRollAcceleration * authority));
	}

	// Local axes: +X right, +Y up, -Z forward. Boost forces full ahead, as without assist.
	private Vector3 CommandedLocalVelocity()
	{
		float forward = _isBoosting ? 1.0f : _input.Throttle;
		return new Vector3(
			ShipInput.Axis(_input.StrafeRight, _input.StrafeLeft),
			ShipInput.Axis(_input.StrafeUp, _input.StrafeDown),
			-forward) * _currentMaxSpeed;
	}

	// Strafe thrusters across the nose; along it the main engine pushes toward -Z and the
	// reverse thrusters toward +Z.
	private void HoldLocalVelocity(Vector3 target, float delta)
	{
		Vector3 local = Transform.Basis.Transposed() * Velocity;
		float strafe = _currentStrafeAcceleration;
		SetLocalVelocity(local, new Vector3(
			AssistAxis(local.X, target.X, ref _intendedVelocity.X, strafe, strafe, delta),
			AssistAxis(local.Y, target.Y, ref _intendedVelocity.Y, strafe, strafe, delta),
			AssistAxis(local.Z, target.Z, ref _intendedVelocity.Z, _currentReverseAcceleration, _currentAcceleration, delta)));
	}

	// One local axis. thrustUp and thrustDown are the thrusters that raise and lower its velocity.
	private float AssistAxis(float velocity, float target, ref float intended, float thrustUp, float thrustDown, float delta)
	{
		float Thrust(float to) => to > velocity ? thrustUp : thrustDown;

		// Intended velocity only counts while the ship still moves that way, and no faster than it does.
		intended = Mathf.Sign(intended) == Mathf.Sign(velocity)
			? Mathf.Sign(velocity) * Mathf.Min(Mathf.Abs(intended), Mathf.Abs(velocity))
			: 0.0f;

		// Above both the intended speed and a target the same way, the velocity is unwanted.
		float wanted = Mathf.Abs(intended);
		if (Mathf.Sign(target) == Mathf.Sign(velocity))
			wanted = Mathf.Max(wanted, Mathf.Abs(target));
		float floor = Mathf.Sign(velocity) * Mathf.Min(wanted, Mathf.Abs(velocity));

		float next = floor != velocity
			? Mathf.MoveToward(velocity, floor, Thrust(floor) * UnintendedBrakeMultiplier * delta)
			: Mathf.MoveToward(velocity, target, Thrust(target) * AssistThrustRatio * delta);

		// What the pilot pushes for becomes intended; pushing against the motion disowns it.
		if (target != 0.0f)
		{
			intended = Mathf.Sign(target) == Mathf.Sign(next)
				? Mathf.Sign(next) * Mathf.Max(Mathf.Abs(intended), Mathf.Min(Mathf.Abs(next), Mathf.Abs(target)))
				: 0.0f;
		}
		return next;
	}

	private void SetLocalVelocity(Vector3 before, Vector3 after)
	{
		float forwardPush = before.Z - after.Z;
		_engineForward = forwardPush > 0.001f;
		_engineBackward = forwardPush < -0.001f;
		Velocity = Transform.Basis * after;
	}

	// Pitch and yaw intent as a share of full deflection, which the widget reaches at the aim
	// circle's edge. Assisted or not, full deflection spends the same angular thrust.
	private static float AimDeflection(float aim) =>
		Mathf.Clamp(aim / MOUSE_SENSITIVITY, -1.0f, 1.0f);

	private void UpdateFlightAssistHud()
	{
		if (_speedBar == null) return;

		string title = _flightAssist ? "SPEED - FA ON" : "SPEED - FA OFF";
		if (title != _speedBarTitle)
		{
			_speedBarTitle = title;
			_speedBar.Title = title;
		}

		// The lever only means something in throttle mode, with the full assist holding it.
		_speedBar.SetMarker(_flightAssist && _throttleMode ? Mathf.Abs(_throttleLever) : null);
	}
}
