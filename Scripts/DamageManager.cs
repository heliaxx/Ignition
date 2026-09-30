using Godot;
using System.Collections.Generic;

// Every hit in the game passes through here. Weapons say who hit what; this decides
// whether that is local business or the server's, and applies it. Gameplay scripts keep
// their plain TakeDamage and never learn there is a network.
public partial class DamageManager : Node
{
	public static DamageManager Instance { get; private set; }

	// Both ships in a ram can detect it — each machine's own ship, or a fighter and the player
	// on one machine — so a pair is only hurt once within this window.
	private const ulong RamCooldownMsec = 500;
	private readonly Dictionary<(ulong, ulong), ulong> _lastRam = new();

	public override void _Ready() => Instance = this;

	// A collision between two ships: both take the same damage, each credited to the other.
	public void ReportRam(Node3D a, Node3D b, float amount)
	{
		int aId = Participants.IdOf(a);
		int bId = Participants.IdOf(b);

		if (!NetworkManager.Instance.IsActive || aId == Participants.None || bId == Participants.None)
		{
			if (RamOnCooldown(a.GetInstanceId(), b.GetInstanceId())) return;
			Report(a, amount, null, b);
			Report(b, amount, null, a);
			return;
		}

		if (NetworkManager.Instance.IsServer)
			ServerRam(aId, bId, amount);
		else
			RpcId(1, MethodName.RequestRam, bId, amount);
	}

	// Client -> server, for a ram the client's own ship detected. The sender is the other party.
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestRam(int otherId, float amount) =>
		ServerRam(Multiplayer.GetRemoteSenderId(), otherId, amount);

	// Server only. Both players' machines usually see the same ram; the first report wins.
	private void ServerRam(int aId, int bId, float amount)
	{
		if (RamOnCooldown((ulong)aId, (ulong)bId)) return;
		Rpc(MethodName.ApplyDamage, aId, amount, bId);
		Rpc(MethodName.ApplyDamage, bId, amount, aId);
	}

	private bool RamOnCooldown(ulong x, ulong y)
	{
		var pair = x < y ? (x, y) : (y, x);
		ulong now = Time.GetTicksMsec();
		if (_lastRam.TryGetValue(pair, out ulong last) && now - last < RamCooldownMsec) return true;
		_lastRam[pair] = now;
		return false;
	}

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
