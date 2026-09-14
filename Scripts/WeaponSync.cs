using Godot;

// Relays shots so every peer sees the tracers. Only the muzzle transform travels — the
// shooter has already applied its own spread, so no seed has to be agreed on.
//
// Relayed bullets are visual: the peer that fired reports its own hits through
// DamageManager, which keeps damage from being counted once per machine.
public partial class WeaponSync : Node
{
	public static WeaponSync Instance { get; private set; }

	public override void _Ready() => Instance = this;

	public void ReportGatlingShot(int shooterId, Transform3D muzzle, Vector3 inheritedVelocity)
	{
		if (!NetworkManager.Instance.IsActive) return;

		if (NetworkManager.Instance.IsServer)
			Rpc(MethodName.SpawnTracer, shooterId, muzzle, inheritedVelocity);
		else
			RpcId(1, MethodName.SubmitShot, muzzle, inheritedVelocity);
	}

	// Client -> server. The sender id is stamped by the server, so a client cannot draw
	// tracers out of somebody else's ship.
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	private void SubmitShot(Transform3D muzzle, Vector3 inheritedVelocity)
	{
		Rpc(MethodName.SpawnTracer, Multiplayer.GetRemoteSenderId(), muzzle, inheritedVelocity);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	private void SpawnTracer(int shooterId, Transform3D muzzle, Vector3 inheritedVelocity)
	{
		// The shooter already spawned this one the moment the trigger went down.
		if (shooterId == NetworkManager.Instance.LocalPeerId) return;

		if (Participants.NodeOf(shooterId) is PlayerShip ship)
			ship.SpawnRelayedShot(muzzle, inheritedVelocity);
	}
}
