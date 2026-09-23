using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class NetworkManager : Node
{
	private const string ProtocolVersion = "4";

	private const double AuthTimeoutSeconds = 5.0;

	public const int DefaultPort = 30500;
	public const int MaxPlayers = 12;

	public static NetworkManager Instance { get; private set; }

	public IPeerFactory PeerFactory { get; set; } = new EnetPeerFactory();

	[Signal] public delegate void PeerJoinedEventHandler(int peerId);
	[Signal] public delegate void PeerLeftEventHandler(int peerId);
	[Signal] public delegate void JoinedServerEventHandler();
	[Signal] public delegate void LeftServerEventHandler(string reason);

	// Who is connected and what they call themselves. The server owns this map and mirrors
	// it to everyone, so a name is known before the ship carrying it exists.
	private readonly Dictionary<int, string> _peers = new();

	// Peers already let in. A relay can deliver the same authentication packet twice.
	private readonly HashSet<int> _authenticated = new();

	private SceneMultiplayer _scene;

	private bool _sessionOpen;

	public bool IsActive => _sessionOpen;

	public bool IsServer => !IsActive || Multiplayer.IsServer();

	// A session is open from the moment a peer is created, which on the relay transports is
	// well before the host answers, and stays open when it never does.
	public bool IsConnected => IsActive && (Multiplayer.IsServer()
		|| Multiplayer.MultiplayerPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected);

	public int LocalPeerId => IsActive ? Multiplayer.GetUniqueId() : 1;

	public IReadOnlyCollection<int> Peers => _peers.Keys;

	public string NameOf(int peerId) => _peers.TryGetValue(peerId, out string name) ? name : "?";

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
		_peers.Clear();
		return true;
	}

	public void Leave()
	{
		if (_sessionOpen)
			Multiplayer.MultiplayerPeer?.Close();

		Multiplayer.MultiplayerPeer = null;
		_sessionOpen = false;
		_peers.Clear();
		_authenticated.Clear();
	}

	private void OnPeerAuthenticating(long id)
	{
		_scene.SendAuth((int)id, ProtocolVersion.ToUtf8Buffer());
	}

	private void OnAuthReceived(int id, byte[] data)
	{
		string theirs = data.GetStringFromUtf8();
		if (theirs == ProtocolVersion)
		{
			if (_authenticated.Add(id)) _scene.CompleteAuth(id);
			return;
		}

		GD.PrintErr($"NetworkManager: peer {id} speaks protocol '{theirs}', we speak '{ProtocolVersion}'");
		Multiplayer.MultiplayerPeer.DisconnectPeer(id);
	}

	private void OnPeerAuthenticationFailed(long id)
	{
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
		if (Multiplayer.IsServer()) BroadcastRoster();
		EmitSignal(SignalName.PeerLeft, (int)id);
	}

	private void OnConnectedToServer()
	{
		_peers[LocalPeerId] = LocalName();
		RpcId(1, MethodName.SubmitName, LocalName());
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
		Leave();
		EmitSignal(SignalName.LeftServer, "connection failed");
	}

	private void OnServerDisconnected()
	{
		Leave();
		EmitSignal(SignalName.LeftServer, "host closed the session");
	}
}
