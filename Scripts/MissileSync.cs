using System.Collections.Generic;
using System.Linq;
using Godot;

// Replicates missiles. The peer that fired one simulates it and streams its transform;
// everyone else shows a frozen copy that cannot steer or detonate.
//
// Same authority split as ships: a missile is an extension of its shooter's movement, not
// the server's. Making it server-simulated would need launch prediction that ships do not
// have either, and two different authority models would be worse than one loose one.
public partial class MissileSync : Node
{
	private const string MissileScene = "res://Scenes/FlightModelMissile.tscn";
	private const double SendInterval = 1.0 / 20.0;
	private const float Smoothing = 15.0f;

	public static MissileSync Instance { get; private set; }

	private sealed class Remote
	{
		public FlightModelMissile Missile;
		public Vector3 Position;
		public Quaternion Rotation;
	}

	// Missiles this peer fired and simulates, keyed by our own sequence number.
	private readonly Dictionary<int, FlightModelMissile> _owned = new();

	// Copies of other peers' missiles. Owner plus that owner's sequence number is unique
	// without anyone having to hand out ids.
	private readonly Dictionary<(int Owner, int Seq), Remote> _remotes = new();

	private int _nextSeq = 1;
	private double _sendTimer;
	private PackedScene _missileScene;

	public override void _Ready() => Instance = this;

	public void Clear()
	{
		_owned.Clear();
		_remotes.Clear();
		_nextSeq = 1;
	}

	// Called by the ship that just fired. The missile already flies locally; this only makes
	// it visible to everyone else.
	public void ReportLaunch(FlightModelMissile missile, Transform3D spawn)
	{
		if (!NetworkManager.Instance.IsActive) return;

		int seq = _nextSeq++;
		_owned[seq] = missile;
		// Detonation ends the copy; TreeExiting covers the missile that expires without ever
		// hitting anything. ReportDespawn ignores the second of the two.
		missile.Detonated += at => ReportDespawn(seq, at);
		// TreeExiting, not TreeExited: the position has to be read while the node is still
		// in the tree.
		missile.TreeExiting += () => ReportDespawn(seq, missile.GlobalPosition);

		if (NetworkManager.Instance.IsServer)
			Rpc(MethodName.SpawnCopy, NetworkManager.Instance.LocalPeerId, seq, spawn);
		else
			RpcId(1, MethodName.SubmitLaunch, seq, spawn);
	}

	// Copies from a player who is gone: nobody is left to report them despawned.
	public void RemoveOwner(int owner)
	{
		foreach ((int Owner, int Seq) key in _remotes.Keys.Where(k => k.Owner == owner).ToList())
		{
			if (IsInstanceValid(_remotes[key].Missile)) _remotes[key].Missile.Vanish();
			_remotes.Remove(key);
		}
	}

	private void ReportDespawn(int seq, Vector3 at)
	{
		if (!_owned.Remove(seq)) return;
		if (!NetworkManager.Instance.IsActive) return;

		if (NetworkManager.Instance.IsServer)
			Rpc(MethodName.DespawnCopy, NetworkManager.Instance.LocalPeerId, seq, at);
		else
			RpcId(1, MethodName.SubmitDespawn, seq, at);
	}

	public override void _PhysicsProcess(double delta)
	{
		AdvanceRemotes((float)delta);

		if (!NetworkManager.Instance.IsActive || _owned.Count == 0) return;

		_sendTimer += delta;
		if (_sendTimer < SendInterval) return;
		_sendTimer = 0.0;

		foreach (KeyValuePair<int, FlightModelMissile> owned in _owned)
		{
			if (!IsInstanceValid(owned.Value) || !owned.Value.IsInsideTree()) continue;
			if (NetworkManager.Instance.IsServer)
				Rpc(MethodName.ApplyState, NetworkManager.Instance.LocalPeerId, owned.Key, owned.Value.GlobalTransform);
			else
				RpcId(1, MethodName.SubmitState, owned.Key, owned.Value.GlobalTransform);
		}
	}

	private void AdvanceRemotes(float delta)
	{
		foreach (Remote remote in _remotes.Values)
		{
			if (!IsInstanceValid(remote.Missile) || !remote.Missile.IsInsideTree()) continue;

			float t = 1.0f - Mathf.Exp(-Smoothing * delta);
			remote.Missile.GlobalPosition = remote.Missile.GlobalPosition.Lerp(remote.Position, t);
			remote.Missile.GlobalBasis = new Basis(
				remote.Missile.GlobalBasis.GetRotationQuaternion().Slerp(remote.Rotation, t));
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SubmitLaunch(int seq, Transform3D spawn) =>
		Rpc(MethodName.SpawnCopy, Multiplayer.GetRemoteSenderId(), seq, spawn);

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
	private void SubmitState(int seq, Transform3D state) =>
		Rpc(MethodName.ApplyState, Multiplayer.GetRemoteSenderId(), seq, state);

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SubmitDespawn(int seq, Vector3 at) =>
		Rpc(MethodName.DespawnCopy, Multiplayer.GetRemoteSenderId(), seq, at);

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SpawnCopy(int owner, int seq, Transform3D spawn)
	{
		if (owner == NetworkManager.Instance.LocalPeerId) return;

		_missileScene ??= GD.Load<PackedScene>(MissileScene);
		var missile = _missileScene.Instantiate<FlightModelMissile>();
		GetTree().CurrentScene.AddChild(missile);
		missile.GlobalTransform = spawn;
		missile.MakeRemote();

		_remotes[(owner, seq)] = new Remote
		{
			Missile = missile,
			Position = spawn.Origin,
			Rotation = spawn.Basis.GetRotationQuaternion(),
		};
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
	private void ApplyState(int owner, int seq, Transform3D state)
	{
		if (!_remotes.TryGetValue((owner, seq), out Remote remote)) return;

		remote.Position = state.Origin;
		remote.Rotation = state.Basis.GetRotationQuaternion();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void DespawnCopy(int owner, int seq, Vector3 at)
	{
		if (!_remotes.Remove((owner, seq), out Remote remote)) return;
		if (!IsInstanceValid(remote.Missile)) return;

		// The copy never detonated, so it plays the blast rather than the launcher's full
		// impact sequence.
		Explosion.SpawnAt(remote.Missile, at);
		remote.Missile.Vanish();
	}
}
