using System.Collections.Generic;
using Godot;

// EOS side of multiplayer, the counterpart of SteamManager: it points NetworkManager at the
// EOS transport once a lobby is open. Logging in and the lobby calls themselves live in
// EosBridge.gd, because the EOS addon's API is asynchronous GDScript that C# cannot await.
public partial class EosManager : Node
{
	public record Lobby(string Id, string Host, int Players);

	public static EosManager Instance { get; private set; }

	public bool IsAvailable => _bridge != null && _bridge.Get("available").AsBool();

	// Filled by the last lobby search.
	public IReadOnlyList<Lobby> Lobbies => _lobbies;

	[Signal] public delegate void LobbiesFoundEventHandler();
	[Signal] public delegate void AvailabilityChangedEventHandler();
	[Signal] public delegate void LobbyOpenedEventHandler();
	[Signal] public delegate void LobbyFailedEventHandler(string reason);

	private readonly List<Lobby> _lobbies = new();
	private Node _bridge;

	public override void _Ready()
	{
		Instance = this;

		_bridge = GetNodeOrNull<Node>("/root/EosBridge");
		if (_bridge == null)
		{
			GD.Print("EosManager: no EosBridge; EOS multiplayer is off");
			return;
		}

		// Logging in takes a moment, so the menu is told when it finishes.
		_bridge.Connect("became_available", Callable.From(() => EmitSignal(SignalName.AvailabilityChanged)));
		_bridge.Connect("lobbies_found", Callable.From<Godot.Collections.Array>(OnLobbiesFound));
		_bridge.Connect("lobby_opened", Callable.From<string, bool>(OnLobbyOpened));
		_bridge.Connect("lobby_failed", Callable.From<string>(
			reason => EmitSignal(SignalName.LobbyFailed, reason)));

		NetworkManager.Instance.LeftServer += _ => LeaveLobby();
	}

	public void HostLobby()
	{
		if (IsAvailable) _bridge.Call("host_lobby", NetworkManager.MaxPlayers);
	}

	public void JoinLobby(string lobbyId)
	{
		if (IsAvailable) _bridge.Call("join_lobby", lobbyId);
	}

	public void RefreshLobbies()
	{
		if (IsAvailable) _bridge.Call("refresh_lobbies");
	}

	public void LeaveLobby()
	{
		if (IsAvailable) _bridge.Call("leave_lobby");
	}

	private void OnLobbiesFound(Godot.Collections.Array lobbies)
	{
		_lobbies.Clear();
		foreach (Variant entry in lobbies)
		{
			Godot.Collections.Dictionary lobby = entry.AsGodotDictionary();
			_lobbies.Add(new Lobby(lobby["id"].AsString(), lobby["host"].AsString(), lobby["players"].AsInt32()));
		}

		EmitSignal(SignalName.LobbiesFound);
	}

	private void OnLobbyOpened(string hostUserId, bool isHost)
	{
		NetworkManager.Instance.PeerFactory = new EosPeerFactory(hostUserId);
		if (isHost ? NetworkManager.Instance.Host() : NetworkManager.Instance.Join(""))
		{
			EmitSignal(SignalName.LobbyOpened);
			return;
		}

		LeaveLobby();
		EmitSignal(SignalName.LobbyFailed, isHost ? "could not host over EOS" : "could not reach the host over EOS");
	}
}
