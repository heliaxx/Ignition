using System.Collections.Generic;
using System.Linq;
using Godot;

// PVP arena. Keeps a fixed world origin — see BaseLevel.ShiftOrigin — so every machine
// agrees on coordinates.
public partial class LevelDeathmatch : BaseLevel
{
	private const string ShipScene = "res://Scenes/PlayerShip.tscn";

	protected override int LocalParticipantId => NetworkManager.Instance.LocalPeerId;
	protected override string LocalParticipantName =>
		NetworkManager.Instance.NameOf(NetworkManager.Instance.LocalPeerId);

	private Node3D _spawnPoints;
	// Spawn point indices in the order the roster takes them at match start.
	private int[] _startOrder;

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

		_startOrder = ShuffledSpawnPoints(MatchManager.Instance.SpawnSeed);
		Player.GlobalTransform = StartTransform(NetworkManager.Instance.LocalPeerId);
		SpawnRemoteShips();
	}

	private void SpawnRemoteShips()
	{
		ShipSync.Instance.Clear();
		ShipSync.Instance.SetLocalShip(PlayerShip);
		MissileSync.Instance.Clear();

		var scene = GD.Load<PackedScene>(ShipScene);
		int localId = NetworkManager.Instance.LocalPeerId;
		IReadOnlyList<int> roster = MatchManager.Instance.Roster;

		for (int i = 0; i < roster.Count; i++)
		{
			int peerId = roster[i];
			if (peerId == localId) continue;

			var ship = scene.Instantiate<PlayerShip>();
			// Before AddChild: _Ready would otherwise hand this ship the viewport camera.
			ship.MakeRemote();
			AddChild(ship);

			ship.GlobalTransform = StartTransform(peerId);
			Participants.Register(ship, peerId, NetworkManager.Instance.NameOf(peerId));
			MatchStats.Register(ship);
			ShipSync.Instance.AddRemoteShip(peerId, ship);
		}
	}

	// Puts a participant back in the arena. The level owns which ship a participant flies
	// A player who left: their ship and any missile still flying for them go, their scoreboard
	// row stays.
	public void RemoveParticipant(int participantId)
	{
		ShipSync.Instance.RemoveRemoteShip(participantId);
		MissileSync.Instance.RemoveOwner(participantId);
		Participants.NodeOf(participantId)?.QueueFree();
	}

	public void Respawn(int participantId, int spawnRoll)
	{
		if (Participants.NodeOf(participantId) is PlayerShip ship)
			ship.Respawn(SpawnTransform(spawnRoll));
	}

	// Every machine shuffles with the same seed, so all agree, and no two ships share a start
	// while there are enough points.
	private Transform3D StartTransform(int peerId)
	{
		int index = MatchManager.Instance.SpawnIndexOf(peerId);
		if (index < 0)
		{
			GD.PushError("LevelDeathmatch: participant has no spawn index, falling back to the first");
			index = 0;
		}

		return _startOrder.Length > 0 ? SpawnTransform(_startOrder[index % _startOrder.Length]) : GlobalTransform;
	}

	private int[] ShuffledSpawnPoints(long seed)
	{
		int[] order = Enumerable.Range(0, _spawnPoints?.GetChildCount() ?? 0).ToArray();
		var rng = new RandomNumberGenerator { Seed = (ulong)seed };
		for (int i = order.Length - 1; i > 0; i--)
		{
			int j = rng.RandiRange(0, i);
			(order[i], order[j]) = (order[j], order[i]);
		}
		return order;
	}

	// Any index wraps onto the spawn points. The ship faces the middle of the arena whichever
	// way the marker points.
	public Transform3D SpawnTransform(int index)
	{
		if (_spawnPoints == null || _spawnPoints.GetChildCount() == 0)
			return GlobalTransform;

		if (_spawnPoints.GetChild(Mathf.PosMod(index, _spawnPoints.GetChildCount())) is not Node3D marker)
			return GlobalTransform;

		Vector3 at = marker.GlobalPosition;
		return new Transform3D(Basis.LookingAt(GlobalPosition - at), at);
	}
}
