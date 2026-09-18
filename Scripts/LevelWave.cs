using Godot;
using System;
using System.Collections.Generic;

public partial class LevelWave : BaseLevel
{
	private const int POOL_SIZE = 20;
	private const float SPAWN_MIN_DISTANCE = 150f;
	private const float SPAWN_MAX_DISTANCE = 300f;

	private EnemySpawner _spawner;
	private int _killCount    = 0;
	private int _highKills    = 0;
	private int _waveNumber   = 1;
	private int _waveSize     = 1;
	private int _aliveInWave  = 0;

	private Label _killLabel;
	private Label _highKillLabel;
	private Label _waveLabel;

	public override void _Ready()
	{
		base._Ready();

		_killLabel     = GetNodeOrNull<Label>("ScoreHUD/KillLabel");
		_highKillLabel = GetNodeOrNull<Label>("ScoreHUD/HighKillLabel");
		_waveLabel     = GetNodeOrNull<Label>("ScoreHUD/WaveLabel");
		_highKills     = (int)(ConfigFileHandler.Instance?.LoadWaveHighScore() ?? 0);

		WirePlayerDeath(OnPlayerDied);

		_spawner = new EnemySpawner
		{
			EnemyScene  = GD.Load<PackedScene>("res://Scenes/BengalFighter.tscn"),
			PortalScene = GD.Load<PackedScene>("res://Scenes/Portal.tscn"),
			PoolSize    = POOL_SIZE
		};
		_spawner.Initialize(NavigationRegion);
		_spawner.EnemyDied += OnEnemyDied;

		StartWave();
	}

	private void StartWave()
	{
		_aliveInWave = _waveSize;
		UpdateHUD();

		for (int i = 0; i < _waveSize; i++)
			SpawnEnemy();
	}

	private void SpawnEnemy()
	{
		Vector3 dir = new Vector3(
			(float)GD.Randf() * 2f - 1f,
			(float)GD.Randf() * 2f - 1f,
			(float)GD.Randf() * 2f - 1f
		).Normalized();
		float dist = (float)GD.RandRange(SPAWN_MIN_DISTANCE, SPAWN_MAX_DISTANCE);
		_spawner.SpawnViaPortal(Player.GlobalPosition + dir * dist, Player);
	}

	private void OnEnemyDied(Fighter enemy)
	{
		_spawner.Recycle(enemy);

		_killCount++;
		_aliveInWave--;

		if (_killCount > _highKills)
		{
			_highKills = _killCount;
			ConfigFileHandler.Instance?.SaveWaveHighScore(_highKills);
		}

		UpdateHUD();

		if (_aliveInWave <= 0)
		{
			_waveNumber++;
			_waveSize++;
			StartWave();
		}
	}

	private void UpdateHUD()
	{
		if (_killLabel != null)
			_killLabel.Text = $"Kills: {_killCount}";
		if (_highKillLabel != null)
			_highKillLabel.Text = $"Best: {_highKills}";
		if (_waveLabel != null)
			_waveLabel.Text = $"Wave {_waveNumber}  ({_aliveInWave} remaining)";
	}

	public int GetKillCount() => _killCount;

	private void OnPlayerDied()
	{
		var deathLabel = Player.GetNodeOrNull<Label>("DeathScreenLayer/DeathScreen/VBoxContainer/DeathLabel");
		if (deathLabel != null)
			deathLabel.Text = $"YOU DIED\nWave {_waveNumber} — Kills: {_killCount}";
	}

	public override void _PhysicsProcess(double delta) { }
}
