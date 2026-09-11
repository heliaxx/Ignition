using Godot;

// Every hit in the game passes through here. Weapons say who hit what; this decides
// whether that is local business or the server's, and applies it. Gameplay scripts keep
// their plain TakeDamage and never learn there is a network.
public partial class DamageManager : Node
{
	public static DamageManager Instance { get; private set; }

	public override void _Ready() => Instance = this;

	public void Report(Node3D target, float amount, CollisionShape3D hitShape, Node3D source)
	{
		if (!NetworkManager.Instance.IsActive)
		{
			ApplyLocal(target, amount, hitShape, source);
			return;
		}

		// Rocks are shared world state, not participants: every machine builds the same field
		// from the match seed, so a hit only has to name which rock and how hard.
		if (target is AsteroidBody)
		{
			ulong asteroidId = AsteroidBody.IdOf(hitShape);
			if (asteroidId == 0UL) return;

			if (NetworkManager.Instance.IsServer)
				ServerAsteroidHit(asteroidId, (int)amount);
			else
				RpcId(1, MethodName.RequestAsteroidHit, asteroidId, (int)amount);
			return;
		}

		int targetId = Participants.IdOf(target);

		// Level hazards and anything else off the roster stay local; nothing replicates them.
		if (targetId == Participants.None)
		{
			ApplyLocal(target, amount, hitShape, source);
			return;
		}

		if (NetworkManager.Instance.IsServer)
			Rpc(MethodName.ApplyDamage, targetId, amount, Participants.IdOf(source));
		else
			RpcId(1, MethodName.RequestDamage, targetId, amount);
	}

	// Client -> server. The server stamps the attacker from the sender id, so a client can
	// only ever claim its own hits, never frame somebody else.
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestDamage(int targetId, float amount)
	{
		Rpc(MethodName.ApplyDamage, targetId, amount, Multiplayer.GetRemoteSenderId());
	}

	// Server -> everyone, so health and death agree on every machine.
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ApplyDamage(int targetId, float amount, int sourceId)
	{
		ApplyLocal(Participants.NodeOf(targetId), amount, null, Participants.NodeOf(sourceId));
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestAsteroidHit(ulong asteroidId, int damage) =>
		ServerAsteroidHit(asteroidId, damage);

	// Server only. One HP ledger decides the moment a rock dies and the others are told the
	// result; replicating the damage would let an unevenly landed hit desync which rocks exist.
	private void ServerAsteroidHit(ulong asteroidId, int damage)
	{
		if (GetTree().GetFirstNodeInGroup("asteroid_field") is not ChunkedAsteroidField field)
			return;

		if (field.DamageAsteroid(asteroidId, damage))
			Rpc(MethodName.ApplyAsteroidDestroyed, asteroidId);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ApplyAsteroidDestroyed(ulong asteroidId)
	{
		if (GetTree().GetFirstNodeInGroup("asteroid_field") is ChunkedAsteroidField field)
			field.DestroyAsteroid(asteroidId);
	}

	private static void ApplyLocal(Node3D target, float amount, CollisionShape3D hitShape, Node3D source)
	{
		if (target is IDamageable damageable)
			damageable.TakeDamage(amount, hitShape, source);
	}
}
