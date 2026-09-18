using Godot;

public partial class LevelSkirmish : BaseLevel
{
	[Export] public int SpawnCount = 8;

	private PackedScene enemyShip = GD.Load<PackedScene>("res://Scenes/BengalFighter.tscn");
	private PackedScene portalScene = GD.Load<PackedScene>("res://Scenes/Portal.tscn");

	private int _killsRemaining;
	private Label _killLabel;
	private ColorRect _endScreen;
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
			SpawnViaPortal(pos);
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
			NavigationRegion.AddChild(enemy);
			enemy.GlobalPosition = pos;
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

	private void OnEnemyKilled()
	{
		_killsRemaining--;
		UpdateKillHUD();

		if (_killsRemaining <= 0)
			ShowEndScreen();
	}

	private void UpdateKillHUD()
	{
		if (_killLabel != null)
			_killLabel.Text = $"Remaining Targets: {_killsRemaining}";
	}

	private void ShowEndScreen()
	{
		GetTree().Paused = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		_endScreen.Visible = true;
	}
}
