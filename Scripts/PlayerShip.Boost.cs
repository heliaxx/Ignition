using Godot;

public partial class PlayerShip
{
	[ExportGroup("Boost")]
	[Export] public float BoostSpeedMultiplier = 1.5f;
	[Export] public float BoostAccelerationMultiplier = 6.0f;
	[Export] public float BoostRotationMultiplier = 1.5f;
	[Export] public float BoostDuration = 3.0f;
	[Export] public float BoostCooldown = 1.0f;
	// Seconds between pressing boost button and the boost actually starting
	[Export] public float BoostDelay = 0.8f;
	[Export] public float BoostSpoolUpTime = 0.3f;
	[Export] public float BoostDecayTime = 1.5f;

	private bool _isBoostCharging = false;
	private float _boostChargeTimer = 0.0f;
	private bool _isBoosting = false;
	private bool _isBoostDecaying = false;
	private float _boostTimer = 0.0f;
	private float _boostDecayTimer = 0.0f;
	private float _boostCooldownTimer = 0.0f;
	private float _currentBoostPower = 0.0f;
	private float _speedAtBoostEnd = 0.0f;

	public bool IsBoosting => _isBoosting || _isBoostDecaying;
	public float BoostCooldownRemaining => _boostCooldownTimer;
	public bool CanBoost => _boostCooldownTimer <= 0.0f && !_isBoosting && !_isBoostCharging;
	public float CurrentBoostPower => _currentBoostPower;

	private void InitBoost()
	{
		_currentBoostPower = 0.0f;
		_boostCooldownTimer = 0.0f;
		ApplyBoostPower();
	}

	private void ActivateBoost()
	{
		_isBoostCharging = true;
		_boostChargeTimer = BoostDelay;
	}

	private void ProcessBoost(float delta)
	{
		// Handle cooldown
		if (_boostCooldownTimer > 0.0f)
		{
			_boostCooldownTimer -= delta;
		}

		if (_isBoostCharging)
		{
			_boostChargeTimer -= delta;
			if (_boostChargeTimer <= 0.0f)
			{
				_isBoostCharging = false;
				_isBoosting = true;
				_isBoostDecaying = false;
				_boostTimer = BoostDuration;
			}
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

		ApplyBoostPower();
	}

	// Scales the ship's base performance by the current boost power.
	private void ApplyBoostPower()
	{
		float boostAccelMult = Mathf.Lerp(1.0f, BoostAccelerationMultiplier, _currentBoostPower);
		float boostSpeedMult = Mathf.Lerp(1.0f, BoostSpeedMultiplier, _currentBoostPower);
		float boostRotMult = Mathf.Lerp(1.0f, BoostRotationMultiplier, _currentBoostPower);

		float engine = MaxSpeed / TimeToMaxSpeed * boostAccelMult;
		_currentAcceleration = engine;
		_currentReverseAcceleration = engine * ReverseThrustRatio;
		_currentStrafeAcceleration = engine * StrafeThrustRatio;
		_currentMaxSpeed = MaxSpeed * boostSpeedMult;
		_currentRollAcceleration = Mathf.DegToRad(RollAcceleration) * boostRotMult;
		_currentPitchAcceleration = Mathf.DegToRad(PitchAcceleration) * boostRotMult;
		_currentYawAcceleration = Mathf.DegToRad(YawAcceleration) * boostRotMult;
		_currentMaxRollSpeed = Mathf.DegToRad(RollRate) * boostRotMult;
		_currentMaxPitchSpeed = Mathf.DegToRad(PitchRate) * boostRotMult;
		_currentMaxYawSpeed = Mathf.DegToRad(YawRate) * boostRotMult;
	}
}
