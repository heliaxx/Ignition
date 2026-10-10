using Godot;

// Direct-IP ENet. Needs the host to be reachable: LAN/port forwarding
public class EnetPeerFactory : IPeerFactory
{
	public MultiplayerPeer CreateHost(int port, int maxPlayers)
	{
		var peer = new ENetMultiplayerPeer();
		Error error = peer.CreateServer(port, maxPlayers);
		if (error == Error.Ok) return peer;
		GD.PrintErr($"EnetPeerFactory: CreateServer({port}) failed with {error}");
		return null;
	}

	public MultiplayerPeer CreateClient(string address, int port)
	{
		var peer = new ENetMultiplayerPeer();
		Error error = peer.CreateClient(address, port);
		if (error == Error.Ok) return peer;

		GD.PrintErr($"EnetPeerFactory: CreateClient({address}:{port}) failed with {error}");
		return null;
	}

	public string IdentityOf(MultiplayerPeer peer, int peerId) =>
		(peer as ENetMultiplayerPeer)?.GetPeer(peerId)?.GetRemoteAddress() ?? "";

	public IPeerFactory MovedTo(string hostIdentity) => null;
}
