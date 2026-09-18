using Godot;

public partial class LevelRush : BaseLevel
{
	[Export] public float ScoreRatePerSecond = 100f;

	[ExportGroup("Health Drain")]
	[Export] public float DrainStartRate = 2f;
	[Export] public float DrainGrowthRate = 8f;

	[ExportGroup("Ring Powerup")]
	[Export] public float RingSpawnInterval = 5f;
	[Export] public float RingSpawnDistance = 500f;
	[Export] public float RingHealAmount = 60f;
	[Export] public float RingScoreMultiplier = 1.5f;
	[Export] public float RingScoreMultiplierDuration = 2f;

	private HealthComponent _playerHealth;

	private float _score = 0f;
	private long _highScore = 0;
	private float _gameTime = 0f;

	private Label _scoreLabel;
	private Label _highScoreLabel;

	private PackedScene _ringScene;
	private Ring _ring;
	private float _ringSpawnTimer = 0f;
	private float _scoreMultiplier = 1f;
	private float _scoreMultiplierTimer = 0f;
	private Label _multiplierLabel;

	public override void _Ready()
	{
		base._Ready();

		_scoreLabel = GetNode<Label>("ScoreHUD/ScoreLabel");
		_highScoreLabel = GetNode<Label>("ScoreHUD/HighScoreLabel");
		_multiplierLabel = GetNodeOrNull<Label>("ScoreHUD/MultiplierLabel");

		_highScore = ConfigFileHandler.Instance?.LoadRushHighScore() ?? 0;

		_playerHealth = PlayerShip.GetNode<HealthComponent>("HealthComponent");

		_ringScene = GD.Load<PackedScene>("res://Scenes/Ring.tscn");
		SpawnRing();
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

		// Score multiplier timer
		if (_scoreMultiplierTimer > 0f)
		{
			_scoreMultiplierTimer -= dt;
			if (_scoreMultiplierTimer <= 0f)
			{
				_scoreMultiplier = 1f;
				_scoreMultiplierTimer = 0f;
			}
		}

		// Scoring
		float speed = PlayerShip.CurrentSpeed;
		float maxSpeed = PlayerShip.BaseMaxSpeed;

		if (speed >= maxSpeed)
		{
			float speedRatio = speed / maxSpeed;
			_score += speedRatio * ScoreRatePerSecond * _scoreMultiplier * dt;

			long scoreInt = (long)_score;
			if (scoreInt > _highScore)
			{
				_highScore = scoreInt;
				ConfigFileHandler.Instance?.SaveRushHighScore(_highScore);
			}
		}

		UpdateHUD();

		// Ring: reposition on timer
		_ringSpawnTimer += dt;
		if (_ringSpawnTimer >= RingSpawnInterval)
		{
			_ringSpawnTimer = 0f;
			SpawnRing();
		}
	}

	private void SpawnRing()
	{
		if (PlayerShip == null) return;

		Vector3 trajectory = PlayerShip.Velocity.Length() > 5f
			? PlayerShip.Velocity.Normalized()
			: -PlayerShip.GlobalTransform.Basis.Z;

		Vector3 spawnPos = PlayerShip.GlobalPosition + trajectory * RingSpawnDistance;

		if (_ring == null)
		{
			_ring = _ringScene.Instantiate<Ring>();
			_ring.Collected += (body) =>
			{
				if (body != PlayerShip) return;
				_playerHealth.Heal(RingHealAmount);
				_scoreMultiplier = RingScoreMultiplier;
				_scoreMultiplierTimer = RingScoreMultiplierDuration;
			};
			AddChild(_ring);
		}

		_ring.GlobalTransform = new Transform3D(BasisFromForward(trajectory), spawnPos);
		_ring.Activate();
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
		_scoreLabel.Text = $"Score: {(long)_score}";
		_highScoreLabel.Text = $"High Score: {_highScore}";
		if (_multiplierLabel != null)
		{
			if (_scoreMultiplierTimer > 0f)
			{
				_multiplierLabel.Text = $"x{_scoreMultiplier:0.0} ({_scoreMultiplierTimer:0.0}s)";
				_multiplierLabel.Visible = true;
			}
			else
			{
				_multiplierLabel.Visible = false;
			}
		}
	}
}
