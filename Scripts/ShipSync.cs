using System.Collections.Generic;
using Godot;

// Replicates ship transforms between peers. The only class that puts ship state on the
// wire: PlayerShip exposes MakeRemote() and its transform and knows nothing about the network.
//
// Client state always goes through the server, which relays it. A client can therefore
// only ever describe its own ship, never move somebody else's.
public partial class ShipSync : Node
{
	private const double SendInterval = 1.0 / 20.0;

	// How fast a stand-in closes on the last state it was told about. High enough to keep
	// up, low enough that a late packet does not snap it.
	private const float Smoothing = 12.0f;

	public static ShipSync Instance { get; private set; }

	private sealed class Remote
	{
		public PlayerShip Ship;
		public Vector3 Position;
		public Quaternion Rotation;
		public Vector3 Velocity;
	}

	// Thrust flags ride along with the transform: the flames read them, and without them a
	// stand-in's engines would sit dark however hard its pilot is burning. Headlights ride
	// along too; sent with every state, a lost toggle corrects itself on the next packet.

	private PlayerShip _local;
	private readonly Dictionary<int, Remote> _remotes = new();
	private double _sendTimer;

	public override void _Ready() => Instance = this;

	// Called by the level as it builds the match; a scene change invalidates every ship.
	public void Clear()
	{
		_local = null;
		_remotes.Clear();
	}

	public void SetLocalShip(PlayerShip ship) => _local = ship;

	public void AddRemoteShip(int peerId, PlayerShip ship)
	{
		_remotes[peerId] = new Remote
		{
			Ship = ship,
			Position = ship.GlobalPosition,
			Rotation = ship.GlobalBasis.GetRotationQuaternion(),
			Velocity = Vector3.Zero,
		};
	}

	public override void _PhysicsProcess(double delta)
	{
		AdvanceRemotes((float)delta);

		if (!NetworkManager.Instance.IsActive) return;
		if (_local == null || !IsInstanceValid(_local)) return;

		_sendTimer += delta;
		if (_sendTimer < SendInterval) return;
		_sendTimer = 0.0;
		SendLocalState();
	}

	private void AdvanceRemotes(float delta)
	{
		foreach (Remote remote in _remotes.Values)
		{
			// A scene change frees ships while this still holds them, and IsInstanceValid
			// stays true for a frame after the node leaves the tree — where reading or
			// writing a global transform fails.
			if (!IsInstanceValid(remote.Ship) || !remote.Ship.IsInsideTree()) continue;

			// Carry the last known velocity forward so motion stays smooth between packets,
			// then ease onto the authoritative state instead of snapping to it.
			remote.Position += remote.Velocity * delta;

			float t = 1.0f - Mathf.Exp(-Smoothing * delta);
			remote.Ship.GlobalPosition = remote.Ship.GlobalPosition.Lerp(remote.Position, t);
			remote.Ship.GlobalBasis = new Basis(
				remote.Ship.GlobalBasis.GetRotationQuaternion().Slerp(remote.Rotation, t));
		}
	}

	private void SendLocalState()
	{
		Vector3 position = _local.GlobalPosition;
		Quaternion rotation = _local.GlobalBasis.GetRotationQuaternion();
		Vector3 velocity = _local.Velocity;

		if (NetworkManager.Instance.IsServer)
			Rpc(MethodName.ApplyShipState, NetworkManager.Instance.LocalPeerId, position, rotation, velocity,
				_local.ThrustingForward, _local.ThrustingBackward, _local.ThrusterBoostOn, _local.HeadlightsOn);
		else
			RpcId(1, MethodName.SubmitShipState, position, rotation, velocity,
				_local.ThrustingForward, _local.ThrustingBackward, _local.ThrusterBoostOn, _local.HeadlightsOn);
	}

	// Client -> server only. The server stamps the sender id itself, so a client cannot
	// claim to be somebody else.
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
	private void SubmitShipState(Vector3 position, Quaternion rotation, Vector3 velocity,
		bool forward, bool backward, bool boosting, bool headlights)
	{
		int sender = Multiplayer.GetRemoteSenderId();
		Rpc(MethodName.ApplyShipState, sender, position, rotation, velocity, forward, backward, boosting, headlights);
	}

	// Server -> everyone. A peer has no entry for its own ship, so its own state echoing
	// back is ignored without a special case.
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
	private void ApplyShipState(int peerId, Vector3 position, Quaternion rotation, Vector3 velocity,
		bool forward, bool backward, bool boosting, bool headlights)
	{
		if (!_remotes.TryGetValue(peerId, out Remote remote)) return;

		remote.Position = position;
		remote.Rotation = rotation;
		remote.Velocity = velocity;

		if (IsInstanceValid(remote.Ship))
		{
			remote.Ship.SetRemoteEngines(velocity, forward, backward, boosting);
			remote.Ship.SetHeadlights(headlights);
		}
	}
}
