using Godot;

// Steam's relay. The lobby stands in for an address, so no player needs a reachable IP.
// GodotSteam is a GDExtension with no C# bindings, hence the calls by name.
public class SteamPeerFactory : IPeerFactory
{
	private readonly ulong _lobbyId;

	public SteamPeerFactory(ulong lobbyId) => _lobbyId = lobbyId;

	public MultiplayerPeer CreateHost(int port, int maxPlayers) => Open("host_with_lobby");

	// Address and port are ignored: the lobby already says which Steam user hosts it.
	public MultiplayerPeer CreateClient(string address, int port) => Open("connect_to_lobby");

	private MultiplayerPeer Open(string method)
	{
		if (ClassDB.Instantiate("SteamMultiplayerPeer").As<GodotObject>() is not MultiplayerPeer peer)
		{
			GD.PrintErr("SteamPeerFactory: SteamMultiplayerPeer is missing; is the GodotSteam addon installed?");
			return null;
		}

		var error = (Error)peer.Call(method, _lobbyId).AsInt32();
		if (error == Error.Ok) return peer;

		GD.PrintErr($"SteamPeerFactory: {method}({_lobbyId}) failed with {error}");
		return null;
	}
}
