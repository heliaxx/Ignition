using Godot;

public partial class PlayerShip
{
	[ExportGroup("Boost")]
	[Export] public float BoostSpeedMultiplier = 1.5f;
	[Export] public float BoostAccelerationMultiplier = 5.0f;
	[Export] public float BoostRotationMultiplier = 1.5f;
	[Export] public float BoostDuration = 3.0f;
	[Export] public float BoostCooldown = 1.0f;
	[Export] public float BoostSpoolUpTime = 0.3f;
	[Export] public float BoostDecayTime = 1.5f;

	private bool _isBoosting = false;
	private bool _isBoostDecaying = false;
	private float _boostTimer = 0.0f;
	private float _boostDecayTimer = 0.0f;
	private float _boostCooldownTimer = 0.0f;
	private float _currentBoostPower = 0.0f;
	private float _speedAtBoostEnd = 0.0f;

	public bool IsBoosting => _isBoosting || _isBoostDecaying;
	public float BoostCooldownRemaining => _boostCooldownTimer;
	public bool CanBoost => _boostCooldownTimer <= 0.0f && !_isBoosting;
	public float CurrentBoostPower => _currentBoostPower;

	private void InitBoost()
	{
		_currentMaxSpeed = MAX_SPEED;
		_currentAcceleration = ACCELERATION;
		_currentRollAcceleration = ROLL_ACCELERATION;
		_currentPitchAcceleration = PITCH_ACCELERATION;
		_currentYawAcceleration = YAW_ACCELERATION;
		_currentMaxRollSpeed = MAX_ROLL_SPEED;
		_currentMaxPitchSpeed = MAX_PITCH_SPEED;
		_currentMaxYawSpeed = MAX_YAW_SPEED;
		_currentBoostPower = 0.0f;
		_boostCooldownTimer = 0.0f;
	}

	private void ActivateBoost()
	{
		_isBoosting = true;
		_isBoostDecaying = false;
		_boostTimer = BoostDuration;
	}

	private void ProcessBoost(float delta)
	{
		// Handle cooldown
		if (_boostCooldownTimer > 0.0f)
		{
			_boostCooldownTimer -= delta;
		}

		// Handle active boost phase
		if (_isBoosting)
		{
			_boostTimer -= delta;

			// Gradual spool up
			if (_currentBoostPower < 1.0f)
			{
				_currentBoostPower = Mathf.MoveToward(_currentBoostPower, 1.0f, delta / BoostSpoolUpTime);
			}

			if (_boostTimer <= 0.0f)
			{
				_isBoosting = false;
				_isBoostDecaying = true;
				_boostDecayTimer = BoostDecayTime;
				_boostCooldownTimer = BoostCooldown;
				_speedAtBoostEnd = Velocity.Length();
			}
		}

		// Handle decay phase - boost power fade out
		else if (_isBoostDecaying)
		{
			_boostDecayTimer -= delta;
			_currentBoostPower = Mathf.Max(0.0f, _boostDecayTimer / BoostDecayTime);

			if (_boostDecayTimer <= 0.0f)
			{
				_isBoostDecaying = false;
				_currentBoostPower = 0.0f;
			}
		}

		// Apply boost power to acceleration, max speed, and rotation
		float boostAccelMult = Mathf.Lerp(1.0f, BoostAccelerationMultiplier, _currentBoostPower);
		float boostSpeedMult = Mathf.Lerp(1.0f, BoostSpeedMultiplier, _currentBoostPower);
		float boostRotMult = Mathf.Lerp(1.0f, BoostRotationMultiplier, _currentBoostPower);

		_currentAcceleration = ACCELERATION * boostAccelMult;
		_currentMaxSpeed = MAX_SPEED * boostSpeedMult;
		_currentRollAcceleration = ROLL_ACCELERATION * boostRotMult;
		_currentPitchAcceleration = PITCH_ACCELERATION * boostRotMult;
		_currentYawAcceleration = YAW_ACCELERATION * boostRotMult;
		_currentMaxRollSpeed = MAX_ROLL_SPEED * boostRotMult;
		_currentMaxPitchSpeed = MAX_PITCH_SPEED * boostRotMult;
		_currentMaxYawSpeed = MAX_YAW_SPEED * boostRotMult;

	}
}
