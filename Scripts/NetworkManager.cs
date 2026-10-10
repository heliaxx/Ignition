using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class NetworkManager : Node
{
	private const string ProtocolVersion = "6";

	// Sent by the host, so the client can tell he's been banned.
	private const string BannedReply = "banned";

	private const double AuthTimeoutSeconds = 5.0;

	// How long a reliable message gets to go out before the connection under it is closed.
	private const double FlushSeconds = 0.5;

	// Moving the host: all peers drop off and reconnect to the new host. The old host stays up for a moment.
	private const double RejoinDelaySeconds = 1.0;
	private const double RejoinTimeoutSeconds = 5.0;
	private const int RejoinAttempts = 5;

	// A lobby whose owner no longer hosts accepts the connection and then never answers.
	private const double JoinTimeoutSeconds = 15.0;

	public const int DefaultPort = 30500;
	public const int MaxPlayers = 12;

	public static NetworkManager Instance { get; private set; }

	public IPeerFactory PeerFactory { get; set; } = new EnetPeerFactory();

	[Signal] public delegate void PeerJoinedEventHandler(int peerId);
	[Signal] public delegate void PeerLeftEventHandler(int peerId);
	[Signal] public delegate void JoinedServerEventHandler();
	[Signal] public delegate void LeftServerEventHandler(string reason);
	[Signal] public delegate void SessionClosedEventHandler();
	// Hosting is moving to another player: everyone drops off and reconnects to them.
	[Signal] public delegate void SessionMovingEventHandler();
	[Signal] public delegate void SessionMovedEventHandler();

	// Who is connected and what they call themselves. The server owns this map and mirrors
	// it to everyone, so a name is known before the ship carrying it exists.
	private readonly Dictionary<int, string> _peers = new();

	// Peers already let in. A relay can deliver the same authentication packet twice.
	private readonly HashSet<int> _authenticated = new();

	// Identities the host has banned. Persists host changes, ends when the session closes.
	private readonly HashSet<string> _banned = new();

	// Peers the host has refused and is about to drop.
	private readonly HashSet<int> _turningAway = new();

	// Reason the host refused a connection.
	private string _dropReason;

	private SceneMultiplayer _scene;

	private bool _sessionOpen;
	// Counts joins, so a timeout can tell whether it still belongs to the current one.
	private int _joinAttempt;
	private bool _joined;
	private bool _moving;
	private int _rejoinAttempt;

	public bool IsActive => _sessionOpen;

	public bool IsMoving => _moving;

	public bool IsServer => !IsActive || Multiplayer.IsServer();

	// A session is open from the moment a peer is created, which on the relay transports is
	// well before the host answers, and stays open when it never does.
	public bool IsConnected => IsActive && (Multiplayer.IsServer()
		|| Multiplayer.MultiplayerPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected);

	public int LocalPeerId => IsActive ? Multiplayer.GetUniqueId() : 1;

	public IReadOnlyCollection<int> Peers => _peers.Keys;

	public string NameOf(int peerId) => _peers.TryGetValue(peerId, out string name) ? name : "?";

	public string IdentityOf(int peerId) =>
		IsActive ? PeerFactory.IdentityOf(Multiplayer.MultiplayerPeer, peerId) : "";

	public override void _Ready()
	{
		Instance = this;
		_scene = (SceneMultiplayer)Multiplayer;

		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += OnPeerDisconnected;
		Multiplayer.ConnectedToServer += OnConnectedToServer;
		Multiplayer.ConnectionFailed += OnConnectionFailed;
		Multiplayer.ServerDisconnected += OnServerDisconnected;

		// Runs before a peer counts as connected, so a version mismatch never reaches a
		// match. A check after connecting would leave an incompatible peer briefly live.
		_scene.PeerAuthenticating += OnPeerAuthenticating;
		_scene.PeerAuthenticationFailed += OnPeerAuthenticationFailed;
		_scene.AuthCallback = Callable.From<int, byte[]>(OnAuthReceived);
		_scene.AuthTimeout = AuthTimeoutSeconds;
	}

	public bool Host(int port = DefaultPort)
	{
		if (IsActive)
		{
			GD.PrintErr("NetworkManager: already connected; call Leave() first");
			return false;
		}

		MultiplayerPeer peer = PeerFactory.CreateHost(port, MaxPlayers);
		if (peer == null)
		{
			GD.PrintErr($"NetworkManager: could not host on port {port}");
			return false;
		}

		Multiplayer.MultiplayerPeer = peer;
		_sessionOpen = true;
		_peers.Clear();
		_peers[LocalPeerId] = LocalName();
		return true;
	}

	public bool Join(string address, int port = DefaultPort)
	{
		if (IsActive)
		{
			GD.PrintErr("NetworkManager: already connected; call Leave() first");
			return false;
		}

		MultiplayerPeer peer = PeerFactory.CreateClient(address, port);
		if (peer == null)
		{
			GD.PrintErr($"NetworkManager: could not reach {address}:{port}");
			return false;
		}

		Multiplayer.MultiplayerPeer = peer;
		_sessionOpen = true;
		_joined = false;
		_peers.Clear();

		// Host moves time their own attempts.
		int attempt = ++_joinAttempt;
		if (!_moving)
			GetTree().CreateTimer(JoinTimeoutSeconds).Timeout += () =>
			{
				if (attempt == _joinAttempt && IsActive && !_joined && !_moving) Drop("the host did not answer");
			};
		return true;
	}

	public void Leave()
	{
		bool wasOpen = _sessionOpen || _moving;
		CloseConnection();
		_moving = false;
		_banned.Clear();
		if (wasOpen) EmitSignal(SignalName.SessionClosed);
	}

	private void CloseConnection()
	{
		if (_sessionOpen)
			Multiplayer.MultiplayerPeer?.Close();

		Multiplayer.MultiplayerPeer = null;
		_sessionOpen = false;
		_peers.Clear();
		_authenticated.Clear();
	}

	private bool IsBanned(int peerId) => Multiplayer.IsServer() && _banned.Contains(IdentityOf(peerId));

	private void OnPeerAuthenticating(long id)
	{
		_scene.SendAuth((int)id, (IsBanned((int)id) ? BannedReply : ProtocolVersion).ToUtf8Buffer());
	}

	private void OnAuthReceived(int id, byte[] data)
	{
		string theirs = data.GetStringFromUtf8();
		if (theirs == ProtocolVersion && !IsBanned(id))
		{
			if (_authenticated.Add(id)) _scene.CompleteAuth(id);
			return;
		}

		if (theirs == BannedReply) _dropReason = "you are banned from this lobby";
		else if (theirs != ProtocolVersion) _dropReason = "the host runs a different game version";
		GD.PrintErr($"NetworkManager: turning away peer {id}, who sent '{theirs}'");

		if (!Multiplayer.IsServer())
		{
			Multiplayer.MultiplayerPeer.DisconnectPeer(id);
			return;
		}
		_turningAway.Add(id);
		GetTree().CreateTimer(FlushSeconds).Timeout += () =>
		{
			if (_turningAway.Remove(id) && IsActive) Multiplayer.MultiplayerPeer.DisconnectPeer(id);
		};
	}

	// Host only. The player is told why before the connection closes.
	// A ban also keeps them out for the rest of the session.
	public void Kick(int peerId, bool ban)
	{
		if (!IsActive || !Multiplayer.IsServer() || peerId == 1 || !_peers.ContainsKey(peerId)) return;

		if (ban) _banned.Add(IdentityOf(peerId));
		RpcId(peerId, MethodName.Removed, (int)(ban ? RemovalReason.Banned : RemovalReason.Kicked));
		GetTree().CreateTimer(FlushSeconds).Timeout += () =>
		{
			if (IsActive && _peers.ContainsKey(peerId)) Multiplayer.MultiplayerPeer.DisconnectPeer(peerId);
		};
	}

	// Sent as a code, not as text: the wording is the removed player's own game's to choose.
	private enum RemovalReason { Kicked, Banned }

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void Removed(int reasonCode)
	{
		string reason = (RemovalReason)reasonCode == RemovalReason.Banned ? "banned by the host" : "kicked by the host";

		// Deferred: the connection must not close while it is still delivering this message.
		Callable.From(() =>
		{
			Leave();
			EmitSignal(SignalName.LeftServer, reason);
		}).CallDeferred();
	}

	// Host only, once the transport has handed its lobby to the new host. Everyone drops the
	// connection and reconnects to them; bans and the lobby itself carry over.
	public void MoveSession(int newHostPeerId, string newHostIdentity)
	{
		if (!IsActive || !Multiplayer.IsServer()) return;
		Rpc(MethodName.SessionMoves, newHostPeerId, newHostIdentity, _banned.ToArray());
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SessionMoves(int newHostPeerId, string newHostIdentity, string[] banned)
	{
		IPeerFactory next = PeerFactory.MovedTo(newHostIdentity);
		if (next == null) return;

		_moving = true;
		_banned.Clear();
		_banned.UnionWith(banned);
		bool becomeHost = LocalPeerId == newHostPeerId;
		EmitSignal(SignalName.SessionMoving);

		// The old host stays up a moment, so this message reaches everyone before it closes.
		if (Multiplayer.IsServer())
			GetTree().CreateTimer(FlushSeconds).Timeout += () => Reconnect(next, becomeHost);
		else
			Callable.From(() => Reconnect(next, becomeHost)).CallDeferred();
	}

	private void Reconnect(IPeerFactory next, bool becomeHost)
	{
		CloseConnection();
		PeerFactory = next;
		if (becomeHost)
		{
			if (Host()) FinishMove();
			else GiveUpMove("could not take over hosting");
			return;
		}

		_rejoinAttempt = 0;
		GetTree().CreateTimer(RejoinDelaySeconds).Timeout += Rejoin;
	}

	private void Rejoin()
	{
		if (!_moving) return;

		int attempt = ++_rejoinAttempt;
		if (!Join(""))
		{
			RetryRejoin();
			return;
		}

		// A relay can sit waiting on a host that never answers instead of failing.
		GetTree().CreateTimer(RejoinTimeoutSeconds).Timeout += () =>
		{
			if (_moving && attempt == _rejoinAttempt && !IsConnected) RetryRejoin();
		};
	}

	private void RetryRejoin()
	{
		CloseConnection();
		if (_rejoinAttempt >= RejoinAttempts)
		{
			GiveUpMove("could not reach the new host");
			return;
		}
		GetTree().CreateTimer(RejoinDelaySeconds).Timeout += Rejoin;
	}

	private void FinishMove()
	{
		_moving = false;
		EmitSignal(SignalName.SessionMoved);
	}

	private void GiveUpMove(string reason)
	{
		Leave();
		EmitSignal(SignalName.LeftServer, reason);
	}

	private void OnPeerAuthenticationFailed(long id)
	{
		_turningAway.Remove((int)id);
		GD.PrintErr($"NetworkManager: peer {id} failed authentication");
	}

	private void OnPeerConnected(long id)
	{
		// Placeholder until the peer says who it is; the roster sync replaces it.
		_peers[(int)id] = "…";
		EmitSignal(SignalName.PeerJoined, (int)id);
	}

	private void OnPeerDisconnected(long id)
	{
		_peers.Remove((int)id);
		_authenticated.Remove((int)id);
		if (Multiplayer.IsServer() && !_moving) BroadcastRoster();
		EmitSignal(SignalName.PeerLeft, (int)id);
	}

	private void OnConnectedToServer()
	{
		_joined = true;
		_dropReason = null;
		_peers[LocalPeerId] = LocalName();
		RpcId(1, MethodName.SubmitName, LocalName());
		if (_moving) FinishMove();
		EmitSignal(SignalName.JoinedServer);
	}

	private static string LocalName() => ConfigFileHandler.Instance.LoadPlayerName();

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SubmitName(string name)
	{
		_peers[Multiplayer.GetRemoteSenderId()] = name;
		BroadcastRoster();
	}

	// The whole map every time: at a dozen peers that is cheaper than tracking deltas, and
	// it also brings a new arrival up to date on everyone already here.
	private void BroadcastRoster()
	{
		int[] ids = _peers.Keys.ToArray();
		string[] names = ids.Select(id => _peers[id]).ToArray();
		Rpc(MethodName.SyncRoster, ids, names);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SyncRoster(int[] ids, string[] names)
	{
		_peers.Clear();
		for (int i = 0; i < ids.Length; i++)
			_peers[ids[i]] = names[i];

		EmitSignal(SignalName.PeerJoined, LocalPeerId);
	}

	private void OnConnectionFailed()
	{
		if (_moving)
		{
			RetryRejoin();
			return;
		}
		Drop("connection failed");
	}

	private void OnServerDisconnected()
	{
		if (_moving)
		{
			RetryRejoin();
			return;
		}
		Drop("host closed the session");
	}

	private void Drop(string fallbackReason)
	{
		string reason = _dropReason ?? fallbackReason;
		_dropReason = null;
		Leave();
		EmitSignal(SignalName.LeftServer, reason);
	}
}
