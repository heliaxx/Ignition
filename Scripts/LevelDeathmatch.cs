using System.Collections.Generic;
using Godot;

// PVP arena. Keeps a fixed world origin — see BaseLevel.ShiftOrigin — so every machine
// agrees on coordinates.
public partial class LevelDeathmatch : BaseLevel
{
	private const string ShipScene = "res://Scenes/Kaito.tscn";

	protected override int LocalParticipantId => NetworkManager.Instance.LocalPeerId;
	protected override string LocalParticipantName =>
		NetworkManager.Instance.NameOf(NetworkManager.Instance.LocalPeerId);

	private Node3D _spawnPoints;

	// Before _Ready: the field generates its chunks there, and children run first, so a seed
	// set any later would arrive after the arena had already been built.
	public override void _EnterTree()
	{
		var asteroids = GetNodeOrNull<ChunkedAsteroidField>("ChunkedAsteroidField");
		if (asteroids != null) asteroids.WorldSeed = MatchManager.Instance.WorldSeed;
	}

	public override void _Ready()
	{
		base._Ready();

		_spawnPoints = GetNodeOrNull<Node3D>("SpawnPoints");
		if (_spawnPoints == null || _spawnPoints.GetChildCount() == 0)
			GD.PushError("LevelDeathmatch: no SpawnPoints, every ship will start on the origin");

		Player.GlobalTransform = SpawnTransform(
			MatchManager.Instance.SpawnIndexOf(NetworkManager.Instance.LocalPeerId));
		SpawnRemoteShips();
	}

	private void SpawnRemoteShips()
	{
		ShipSync.Instance.Clear();
		ShipSync.Instance.SetLocalShip(PlayerKaito);
		MissileSync.Instance.Clear();

		var scene = GD.Load<PackedScene>(ShipScene);
		int localId = NetworkManager.Instance.LocalPeerId;
		IReadOnlyList<int> roster = MatchManager.Instance.Roster;

		for (int i = 0; i < roster.Count; i++)
		{
			int peerId = roster[i];
			if (peerId == localId) continue;

			var ship = scene.Instantiate<Kaito>();
			// Before AddChild: _Ready would otherwise hand this ship the viewport camera.
			ship.MakeRemote();
			AddChild(ship);

			ship.GlobalTransform = SpawnTransform(i);
			Participants.Register(ship, peerId, NetworkManager.Instance.NameOf(peerId));
			MatchStats.Register(ship);
			ShipSync.Instance.AddRemoteShip(peerId, ship);
		}
	}

	// Puts a participant back in the arena. The level owns which ship a participant flies
	public void Respawn(int participantId)
	{
		if (Participants.NodeOf(participantId) is Kaito ship)
			ship.Respawn(SpawnTransform(MatchManager.Instance.SpawnIndexOf(participantId)));
	}

	// Where a participant belongs at match start and on every respawn.
	public Transform3D SpawnTransform(int index)
	{
		if (_spawnPoints == null || _spawnPoints.GetChildCount() == 0)
			return GlobalTransform;

		if (index < 0)
		{
			GD.PushError("LevelDeathmatch: participant has no spawn index, falling back to the first");
			index = 0;
		}

		return _spawnPoints.GetChild(index % _spawnPoints.GetChildCount()) is Node3D marker
			? marker.GlobalTransform
			: GlobalTransform;
	}
}
