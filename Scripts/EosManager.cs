using System.Collections.Generic;
using Godot;

// EOS side of multiplayer, the counterpart of SteamManager: it points NetworkManager at the
// EOS transport once a lobby is open. Logging in and the lobby calls themselves live in
// EosBridge.gd, because the EOS addon's API is asynchronous GDScript that C# cannot await.
public partial class EosManager : Node
{
	public record Lobby(string Id, LobbyInfo Info);

	public static EosManager Instance { get; private set; }

	public bool IsAvailable => _bridge != null && _bridge.Get("available").AsBool();

	// Hosting or joined over EOS.
	public bool InLobby { get; private set; }

	// Filled by the last lobby search.
	public IReadOnlyList<Lobby> Lobbies => _lobbies;

	[Signal] public delegate void LobbiesFoundEventHandler();
	[Signal] public delegate void AvailabilityChangedEventHandler();
	[Signal] public delegate void LobbyOpenedEventHandler();
	[Signal] public delegate void LobbyFailedEventHandler(string reason);

	private readonly List<Lobby> _lobbies = new();
	private Node _bridge;

	// What the lobby attributes last said, so they are only rewritten when something changed.
	private string _published = "";

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

	// Only the host writes the lobby attributes, others just read them.
	public override void _Process(double delta)
	{
		if (!InLobby || !NetworkManager.Instance.IsActive || !NetworkManager.Instance.IsServer) return;

		Dictionary<string, string> data = LobbyInfo.Current().ToData();
		string snapshot = string.Join("\n", data.Values);
		if (snapshot == _published) return;

		_published = snapshot;
		var attributes = new Godot.Collections.Dictionary();
		foreach (var (key, value) in data) attributes[key] = value;
		_bridge.Call("publish", attributes);
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
		InLobby = false;
		_published = "";
		if (IsAvailable) _bridge.Call("leave_lobby");
	}

	private void OnLobbiesFound(Godot.Collections.Array lobbies)
	{
		_lobbies.Clear();
		foreach (Variant entry in lobbies)
		{
			Godot.Collections.Dictionary lobby = entry.AsGodotDictionary();
			Godot.Collections.Dictionary data = lobby["data"].AsGodotDictionary();
			_lobbies.Add(new Lobby(lobby["id"].AsString(),
				LobbyInfo.FromData(key => data.ContainsKey(key) ? data[key].AsString() : null)));
		}

		EmitSignal(SignalName.LobbiesFound);
	}

	private void OnLobbyOpened(string hostUserId, bool isHost)
	{
		NetworkManager.Instance.PeerFactory = new EosPeerFactory(hostUserId);
		if (isHost ? NetworkManager.Instance.Host() : NetworkManager.Instance.Join(""))
		{
			InLobby = true;
			EmitSignal(SignalName.LobbyOpened);
			return;
		}

		LeaveLobby();
		EmitSignal(SignalName.LobbyFailed, isHost ? "could not host over EOS" : "could not reach the host over EOS");
	}
}
