using Godot;

// Steam's relay. The lobby stands in for an address, so no player needs a reachable IP.
// GodotSteam is a GDExtension with no C# bindings, hence the calls by name.
public class SteamPeerFactory : IPeerFactory
{
	private readonly ulong _lobbyId;

	private readonly ulong _hostSteamId;

	public SteamPeerFactory(ulong lobbyId, ulong hostSteamId = 0)
	{
		_lobbyId = lobbyId;
		_hostSteamId = hostSteamId;
	}

	public MultiplayerPeer CreateHost(int port, int maxPlayers) => Open("create_host");

	// Address and port are ignored: the lobby already says which Steam user hosts it.
	public MultiplayerPeer CreateClient(string address, int port)
	{
		long host = _hostSteamId != 0
			? (long)_hostSteamId
			: Engine.GetSingleton("Steam").Call("getLobbyOwner", _lobbyId).AsInt64();
		return Open("create_client", host);
	}

	public string IdentityOf(MultiplayerPeer peer, int peerId) =>
		peer.Call("get_steam_id_for_peer_id", peerId).AsInt64().ToString();

	public IPeerFactory MovedTo(string hostIdentity) =>
		ulong.TryParse(hostIdentity, out ulong id) ? new SteamPeerFactory(_lobbyId, id) : null;

	private MultiplayerPeer Open(string method, params Variant[] args)
	{
		if (ClassDB.Instantiate("SteamMultiplayerPeer").As<GodotObject>() is not MultiplayerPeer peer)
		{
			GD.PrintErr("SteamPeerFactory: SteamMultiplayerPeer is missing; is the GodotSteam addon installed?");
			return null;
		}

		var error = (Error)peer.Call(method, args).AsInt32();
		if (error == Error.Ok) return peer;

		GD.PrintErr($"SteamPeerFactory: {method} for lobby {_lobbyId} failed with {error}");
		return null;
	}
}
