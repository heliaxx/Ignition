using Godot;

// The EOS relay. Peers find each other by product user id on a named socket, so no player
// needs a reachable IP.
public class EosPeerFactory : IPeerFactory
{
	// Both ends must name the same socket to meet on it.
	private const string SocketId = "ignition";

	private readonly string _hostUserId;

	public EosPeerFactory(string hostUserId) => _hostUserId = hostUserId;

	public MultiplayerPeer CreateHost(int port, int maxPlayers)
	{
		MultiplayerPeer peer = New();
		if (peer == null) return null;

		// Without this the host ignores everyone trying to reach it.
		peer.Call("set_auto_accept_connection_requests", true);
		return Open(peer, "create_server", SocketId);
	}

	// Address and port are ignored: the lobby already says which EOS user hosts it.
	public MultiplayerPeer CreateClient(string address, int port)
	{
		MultiplayerPeer peer = New();
		return peer == null ? null : Open(peer, "create_client", SocketId, _hostUserId);
	}

	private static MultiplayerPeer New()
	{
		if (ClassDB.Instantiate("EOSGMultiplayerPeer").As<GodotObject>() is MultiplayerPeer peer) return peer;

		GD.PrintErr("EosPeerFactory: EOSGMultiplayerPeer is missing; is the EOS addon installed?");
		return null;
	}

	private static MultiplayerPeer Open(MultiplayerPeer peer, string method, params Variant[] args)
	{
		var error = (Error)peer.Call(method, args).AsInt32();
		if (error == Error.Ok) return peer;

		GD.PrintErr($"EosPeerFactory: {method} failed with {error}");
		return null;
	}
}
