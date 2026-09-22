using System.Linq;
using Godot;

public partial class LevelSkirmish : BaseLevel
{
	[Export] public int SpawnCount = 8;

	// Seconds between portals, so the ships do not all arrive in the same frame.
	private const float PortalStagger = 0.15f;
	private const float EndScreenDelay = 2.5f;

	private PackedScene enemyShip = GD.Load<PackedScene>("res://Scenes/BengalFighter.tscn");
	private PackedScene portalScene = GD.Load<PackedScene>("res://Scenes/Portal.tscn");

	private int _killsRemaining;
	private Label _killLabel;
	private ColorRect _endScreen;
	private Label _resultLabel;
	private Button _freeRoamButton;
	private Button _restartButton;
	private bool _over;
	private AudioStreamPlayer _clickSound;

	public override void _Ready()
	{
		base._Ready();

		_killLabel = GetNodeOrNull<Label>("ScoreHUD/KillLabel");
		_killsRemaining = SpawnCount;
		UpdateKillHUD();

		// Setup click sound
		_clickSound = new AudioStreamPlayer();
		_clickSound.Bus = "SFX";
		AddChild(_clickSound);
		_clickSound.Stream = (AudioStream)GD.Load("res://Imports/Sounds/click_sound_menu2.mp3");

		// Setup end screen buttons
		_endScreen = GetNode<ColorRect>("EndScreenLayer/EndScreen");
		var mainMenuButton = _endScreen.GetNode<Button>("VBoxContainer/MainMenuButton");
		var freeRoamButton = _endScreen.GetNode<Button>("VBoxContainer/FreeRoamButton");
		_resultLabel = _endScreen.GetNode<Label>("VBoxContainer/CompletionLabel");
		_freeRoamButton = freeRoamButton;
		_restartButton = _endScreen.GetNode<Button>("VBoxContainer/RestartButton");
		_restartButton.Pressed += () =>
		{
			_clickSound.Play();
			GetTree().Paused = false;
			GetTree().ReloadCurrentScene();
		};
		_restartButton.MouseEntered += () => _clickSound.Play();

		mainMenuButton.Pressed += () => { _clickSound.Play(); GetTree().ChangeSceneToFile("res://Scenes/Menu.tscn"); };
		mainMenuButton.MouseEntered += () => _clickSound.Play();

		freeRoamButton.Pressed += () => 
		{ 
			_clickSound.Play();
			_endScreen.Visible = false;
			GetTree().Paused = false;
			Input.MouseMode = Input.MouseModeEnum.Captured;
		};
		freeRoamButton.MouseEntered += () => _clickSound.Play();

		SpawnEnemies();
	}

	private void SpawnEnemies()
	{
		var spawnArea = GetNodeOrNull<Area3D>("SpawnArea");
		var cylinder = spawnArea?.GetNodeOrNull<CollisionShape3D>("CollisionShape3D")?.Shape as CylinderShape3D;
		float radius = cylinder?.Radius ?? 600f;
		float halfHeight = (cylinder?.Height ?? 0f) / 2f;

		for (int i = 0; i < SpawnCount; i++)
		{
			float angle = Mathf.Tau / SpawnCount * i;
			float y = (float)GD.RandRange(-halfHeight, halfHeight);
			Vector3 localPos = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
			Vector3 pos = spawnArea.GlobalTransform * localPos;
			GetTree().CreateTimer(i * PortalStagger).Timeout += () => SpawnViaPortal(pos);
		}
	}

	private void SpawnViaPortal(Vector3 pos)
	{
		var portal = portalScene.Instantiate<Portal>();
		NavigationRegion.AddChild(portal);
		portal.GlobalPosition = pos;
		if (Player != null)
		{
			Vector3 toPlayer = (Player.GlobalPosition - pos).Normalized();
			Vector3 up = Mathf.Abs(toPlayer.Dot(Vector3.Up)) < 0.99f ? Vector3.Up : Vector3.Forward;
			portal.LookAt(Player.GlobalPosition, up);
		}

		portal.OpenThenClose(() =>
		{
			var enemy = enemyShip.Instantiate<Fighter>();
			// Placed before it enters the tree: a body moved after joining the physics world pays
			// for it with a very slow first collision query.
			enemy.Position = NavigationRegion.ToLocal(pos);
			NavigationRegion.AddChild(enemy);
			enemy.Died += () =>
			{
				OnEnemyKilled();
				enemy.QueueFree();
			};

			var returnToCenter = enemy.GetNodeOrNull<ReturnToCenter>("StateMachine/ReturnToCenter");
			if (returnToCenter != null)
				returnToCenter.CenterPoint = pos;
		});
	}

	public override void _Process(double delta)
	{
		base._Process(delta);

		// Out of ammo with targets left is a lost mission, once nothing already fired can still hit.
		if (!_over && _killsRemaining > 0 && PlayerShip != null && PlayerShip.OutOfAmmo
			&& !GetChildren().OfType<FlightModelMissile>().Any(m => !m.IsSpent))
			EndMission();
	}

	private void OnEnemyKilled()
	{
		_killsRemaining--;
		UpdateKillHUD();

		if (_killsRemaining <= 0)
			EndMission();
	}

	// The outcome is read only when the screen comes up, so a bullet still in the air when the
	// ammo ran out can yet win the mission.
	private void EndMission()
	{
		if (_over) return;
		_over = true;
		GetTree().CreateTimer(EndScreenDelay).Timeout += ShowEndScreen;
	}

	private void UpdateKillHUD()
	{
		if (_killLabel != null)
			_killLabel.Text = $"Remaining Targets: {_killsRemaining}";
	}

	private void ShowEndScreen()
	{
		bool won = _killsRemaining <= 0;
		if (!won) _resultLabel.Text = "MISSION FAILED\nOUT OF AMMO";
		_freeRoamButton.Visible = won;
		_restartButton.Visible = !won;

		GetTree().Paused = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		_endScreen.Visible = true;
	}
}
