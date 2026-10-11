using Godot;
using System.Collections.Generic;

public partial class LevelRush : BaseLevel
{
	[Export] public float ScoreRatePerSecond = 100f;
	[Export] public float SpeedModifierAtRest = 0f;
	[Export] public float SpeedModifierAtBoost = 2f;

	[ExportGroup("Health Drain")]
	[Export] public float DrainStartRate = 2f;
	[Export] public float DrainGrowthRate = 8f;

	[ExportGroup("Ring Course")]
	[Export] public int RingsAhead = 3;
	[Export] public float RingSpacing = 1000f;
	[Export] public float RingLateralSpread = 150f;
	[Export] public float RingClearance = 80f;
	[Export] public float RingPassSlack = 40f;       // past the ring's plane by this much counts as missed
	[Export] public float RingLostDistance = 1500f;  // this far from the next ring, the course restarts ahead
	[Export] public float RingHealAmount = 50f;
	[Export] public float RingHealPerCombo = 2.5f;

	[ExportGroup("Combo")]
	[Export] public float ComboStep = 0.25f;
	[Export] public float ComboMaxMultiplier = 5f;

	private const int PlacementAttempts = 20;

	private HealthComponent _playerHealth;

	private float _score = 0f;
	private long _highScore = 0;
	private float _gameTime = 0f;

	private Label _scoreLabel;
	private Label _highScoreLabel;
	private Label _multiplierLabel;
	private Label _speedLabel;
	private RingIndicator _ringIndicator;

	private ChunkedAsteroidField _field;
	private readonly List<Ring> _course = new();
	private Vector3 _courseHeading;
	private int _combo = 0;

	private float ScoreMultiplier => Mathf.Min(1f + ComboStep * _combo, ComboMaxMultiplier);

	public override void _Ready()
	{
		base._Ready();

		_scoreLabel = GetNode<Label>("ScoreHUD/ScoreLabel");
		_highScoreLabel = GetNode<Label>("ScoreHUD/HighScoreLabel");
		_multiplierLabel = GetNodeOrNull<Label>("ScoreHUD/MultiplierLabel");
		_speedLabel = GetNodeOrNull<Label>("ScoreHUD/SpeedLabel");
		_ringIndicator = GetNodeOrNull<RingIndicator>("ScoreHUD/RingIndicator");
		_field = GetNodeOrNull<ChunkedAsteroidField>("ChunkedAsteroidField");

		_highScore = ConfigFileHandler.Instance?.LoadRushHighScore() ?? 0;

		_playerHealth = PlayerShip.GetNode<HealthComponent>("HealthComponent");

		var ringScene = GD.Load<PackedScene>("res://Scenes/Ring.tscn");
		for (int i = 0; i < RingsAhead; i++)
		{
			var ring = ringScene.Instantiate<Ring>();
			// Deferred: the ring is collected inside a physics callback, where it can't be re-armed.
			ring.Collected += (body) =>
			{
				if (body == PlayerShip) Callable.From(() => CollectRing(ring)).CallDeferred();
			};
			AddChild(ring);
			_course.Add(ring);
		}
		BuildCourse();
		UpdateHUD();
	}

	public override void _Process(double delta)
	{
		base._Process(delta);

		if (PlayerShip == null || _playerHealth == null || _playerHealth.IsDead)
			return;

		float dt = (float)delta;
		_gameTime += dt;

		// Health drain: rate linearly increases with time
		float drainRate = DrainStartRate + DrainGrowthRate * (_gameTime / 60f);
		_playerHealth.TakeDamage(drainRate * dt);

		_score += SpeedModifier() * ScoreRatePerSecond * ScoreMultiplier * dt;

		long scoreInt = (long)_score;
		if (scoreInt > _highScore)
		{
			_highScore = scoreInt;
			ConfigFileHandler.Instance?.SaveRushHighScore(_highScore);
		}

		CheckNextRing();
		UpdateHUD();
	}

	// SpeedModifierAtRest when still, 1 at max speed, SpeedModifierAtBoost at full boost speed.
	private float SpeedModifier()
	{
		float speed = PlayerShip.CurrentSpeed;
		float maxSpeed = PlayerShip.BaseMaxSpeed;
		if (speed <= maxSpeed)
			return Mathf.Lerp(SpeedModifierAtRest, 1f, speed / maxSpeed);

		float boostSpeed = maxSpeed * PlayerShip.BoostSpeedMultiplier;
		return Mathf.Lerp(1f, SpeedModifierAtBoost, Mathf.Min((speed - maxSpeed) / (boostSpeed - maxSpeed), 1f));
	}

	private void CheckNextRing()
	{
		Ring next = _course[0];
		// Hidden means collected, and the deferred CollectRing will move it on.
		if (!next.Visible) return;

		Vector3 fromRing = PlayerShip.GlobalPosition - next.Center;
		if (fromRing.Dot(next.GlobalBasis.X) > RingPassSlack)
		{
			_combo = 0;
			AdvanceCourse();
		}
		else if (fromRing.Length() > RingLostDistance)
		{
			_combo = 0;
			BuildCourse();
		}
	}

	private void CollectRing(Ring ring)
	{
		int index = _course.IndexOf(ring);
		// Flying through a later ring means the ones before it were skipped.
		if (index > 0) _combo = 0;
		_combo++;
		// Grows with the combo until the multiplier caps.
		float comboCap = (ComboMaxMultiplier - 1f) / ComboStep;
		_playerHealth.Heal(RingHealAmount + RingHealPerCombo * Mathf.Min(_combo - 1, comboCap));

		for (int i = 0; i <= index; i++)
			AdvanceCourse();
	}

	private void BuildCourse()
	{
		_courseHeading = PlayerShip.Velocity.Length() > 5f
			? PlayerShip.Velocity.Normalized()
			: -PlayerShip.GlobalTransform.Basis.Z;

		Vector3 from = PlayerShip.GlobalPosition;
		foreach (Ring ring in _course)
		{
			PlaceRing(ring, from);
			from = ring.Center;
		}
	}

	// Moves the ring just passed to the far end of the course.
	private void AdvanceCourse()
	{
		Vector3 from = _course[^1].Center;
		Ring ring = _course[0];
		_course.RemoveAt(0);
		PlaceRing(ring, from);
		_course.Add(ring);
	}

	// One step along the heading, pushed sideways at random. Keeps the try with the most room
	// around it.
	private void PlaceRing(Ring ring, Vector3 from)
	{
		Vector3 best = from + _courseHeading * RingSpacing;
		float bestRoom = float.MinValue;
		for (int attempt = 0; attempt < PlacementAttempts && bestRoom < RingClearance; attempt++)
		{
			Vector3 candidate = from + _courseHeading * RingSpacing
				+ RandomSideways() * (float)GD.RandRange(0.0, RingLateralSpread);
			float room = _field?.ClearanceAt(candidate) ?? float.MaxValue;
			if (room > bestRoom)
			{
				bestRoom = room;
				best = candidate;
			}
		}

		ring.GlobalTransform = new Transform3D(BasisFromForward((best - from).Normalized()), best);
		ring.GlobalPosition += best - ring.Center;
		ring.Activate();
	}

	private Vector3 RandomSideways()
	{
		var random = new Vector3((float)GD.Randfn(0, 1), (float)GD.Randfn(0, 1), (float)GD.Randfn(0, 1));
		return (random - _courseHeading * random.Dot(_courseHeading)).Normalized();
	}

	private static Basis BasisFromForward(Vector3 forward)
	{
		Vector3 refUp = Mathf.Abs(forward.Dot(Vector3.Up)) < 0.99f ? Vector3.Up : Vector3.Right;
		Vector3 right = forward.Cross(refUp).Normalized();
		Vector3 up    = right.Cross(forward).Normalized();
		return new Basis(forward, up, right);
	}

	private void UpdateHUD()
	{
		_scoreLabel.Text = $"Current score: {(long)_score}";
		_highScoreLabel.Text = $"Max score: {_highScore}";
		if (_speedLabel != null)
			_speedLabel.Text = $"Speed: x{SpeedModifier():0.00}";
		if (_multiplierLabel != null)
			_multiplierLabel.Text = $"Checkpoints: x{ScoreMultiplier:0.00}";
		if (_ringIndicator != null)
		{
			_ringIndicator.Target = _course[0].Center;
			_ringIndicator.SecondTarget = _course.Count > 1 ? _course[1].Center : null;
		}
	}
}
