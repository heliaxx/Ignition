using Godot;

public partial class BaseLevel : Node3D
{
	protected Node3D Player;
	protected PlayerShip PlayerShip;
	public PlayerShip LocalShip => PlayerShip;
	protected MusicManager MusicManager;
	protected Node3D NavigationRegion;

	[Export] public float OriginShiftThreshold = 5000f;

	// Shifting rebases the world on the local player, so two machines would disagree about
	// every world coordinate. Networked levels keep a fixed origin and stay bounded instead.
	[Export] public bool ShiftOrigin = true;

	private float _originShiftThresholdSq;

	// Id for the ship this machine flies. None lets Participants assign from its local
	// counter, which is right offline; a networked level overrides it with the peer id so
	// both machines name the same ship the same way.
	protected virtual int LocalParticipantId => Participants.None;

	// Null offline, where the node name will do.
	protected virtual string LocalParticipantName => null;

	private static readonly (string Path, float Scale)[] PrewarmScenes =
	{
		("res://Scenes/Bullet.tscn", 1f),
		("res://Scenes/FlightModelMissile.tscn", 1f),
		("res://Scenes/BigExplosionSpace.tscn", 1f),
		("res://Scenes/LaserWeapon.tscn", 1f),
		("res://Scenes/Bengal.tscn", 0.01f),
	};

	public override void _Ready()
	{
		EventBus.ClearAll();
		Participants.Reset();
		MatchStats.Reset();
		Player = GetNode<Node3D>("Player");
		PlayerShip = Player as PlayerShip;
		// Ships register themselves in _Ready, but children run before the level does,
		// so the player would have been wiped by the reset above. Registering the id first
		// means the MatchStats call below finds it rather than assigning a local one.
		Participants.Register(PlayerShip, LocalParticipantId, LocalParticipantName);
		MatchStats.Register(PlayerShip);
		NavigationRegion = this;
		MusicManager = GetNode<MusicManager>("/root/MusicManager");
		MusicManager.StopMusic();
		SyncDirectionalLight();
		_originShiftThresholdSq = OriginShiftThreshold * OriginShiftThreshold;
		PrewarmEffectShaders();
	}

	private void PrewarmEffectShaders()
	{
		var cam = GetViewport()?.GetCamera3D();
		Vector3 basePos = cam != null
			? cam.GlobalPosition - cam.GlobalTransform.Basis.Z * 20f
			: Player.GlobalPosition;

		foreach ((string path, float scale) in PrewarmScenes)
		{
			var scene = GD.Load<PackedScene>(path);
			if (scene == null) continue;

			var inst = scene.Instantiate();
			AddChild(inst);
			if (inst is Node3D n3d)
			{
				n3d.GlobalPosition = basePos;
				n3d.Scale = Vector3.One * scale;
			}
			FreezeSubtree(inst);
			ForceParticlesOn(inst);
			_prewarmInstances.Add(inst);
		}
	}

	private readonly System.Collections.Generic.List<Node> _prewarmInstances = new();
	private int _prewarmFramesLeft = 2;

	private void TickPrewarmCleanup()
	{
		if (_prewarmInstances.Count == 0) return;

		if (--_prewarmFramesLeft > 0) return;

		foreach (Node inst in _prewarmInstances)
			if (GodotObject.IsInstanceValid(inst))
				inst.QueueFree();
		_prewarmInstances.Clear();
	}

	private static void FreezeSubtree(Node node)
	{
		node.ProcessMode = Node.ProcessModeEnum.Disabled;
		foreach (Node child in node.GetChildren())
			FreezeSubtree(child);
	}

	private static void ForceParticlesOn(Node node)
	{
		if (node is GpuParticles3D p)
		{
			p.ProcessMode = Node.ProcessModeEnum.Inherit;
			p.Emitting = true;
		}
		if (node is Node3D vis)
			vis.Visible = true;
		foreach (Node child in node.GetChildren())
			ForceParticlesOn(child);
	}

	public override void _Process(double delta)
	{
		TickPrewarmCleanup();

		if (ShiftOrigin && Player != null && Player.GlobalPosition.LengthSquared() > _originShiftThresholdSq)
			ShiftWorldOrigin();
	}

	private void ShiftWorldOrigin()
	{
		Vector3 offset = Player.GlobalPosition;

		foreach (Node child in GetChildren())
		{
			if (child is Node3D node && child != Player)
				node.GlobalPosition -= offset;
		}

		Player.GlobalPosition = Vector3.Zero;
	}

	protected void SyncDirectionalLight()
	{
		var worldEnv = GetNode<WorldEnvironment>("WorldEnvironment");
		var light = GetNode<DirectionalLight3D>("DirectionalLight3D");

		if (worldEnv?.Environment?.Sky?.SkyMaterial is ShaderMaterial skyMat)
		{
			var sunPos = (Vector3)skyMat.GetShaderParameter("star_pos");
			var sunDir = sunPos.Normalized();
			light.LookAtFromPosition(Vector3.Zero, -sunDir, Vector3.Up);
		}
	}

	protected void WirePlayerDeath(System.Action onDied)
	{
		if (PlayerShip != null)
		{
			var health = PlayerShip.GetNode<HealthComponent>("HealthComponent");
			health.Died += () => onDied();
		}
	}
}
