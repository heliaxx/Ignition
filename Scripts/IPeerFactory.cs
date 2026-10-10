using Godot;

// The only seam that knows which transport carries the game. ENet in the open source
// build; the Steam build supplies its own implementation and nothing else changes.
// Returns null when the peer cannot be created, so callers report rather than crash.
public interface IPeerFactory
{
	MultiplayerPeer CreateHost(int port, int maxPlayers);
	MultiplayerPeer CreateClient(string address, int port);

	// Who a connected peer is beyond this session's peer id, which changes on every connection:
	// what a ban holds on to and what hosting is handed over to.
	string IdentityOf(MultiplayerPeer peer, int peerId);

	// The factory that reaches the session once the given identity hosts it, or null when this
	// transport cannot move its host.
	IPeerFactory MovedTo(string hostIdentity);
}
