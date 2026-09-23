using System.Collections.Generic;
using Godot;

// Steam side of multiplayer: starts Steam, owns the lobby a session runs in, and points
// NetworkManager at the Steam transport. Carries no RPCs; it only opens the connection.
// GodotSteam is a GDExtension with no C# bindings, hence the calls by name.
public partial class SteamManager : Node
{
	// Valve's test app. Every game using it shares one lobby pool, hence the tag filter.
	private const int AppId = 480;
	private const string GameTag = "heliaxx_ignition";

	// Steam's lobby types and its "everything went fine" results.
	private const int PublicLobby = 2;

	// Steam otherwise prefers nearby lobbies and hides the rest of the world's.
	private const int Worldwide = 3;
	private const int ResultOk = 1;
	private const int JoinedOk = 1;

	public record Lobby(ulong Id, string Host, int Players);

	public static SteamManager Instance { get; private set; }

	public bool IsAvailable { get; private set; }
	public ulong LobbyId { get; private set; }

	// Filled by the last lobby search.
	public IReadOnlyList<Lobby> Lobbies => _lobbies;

	[Signal] public delegate void LobbiesFoundEventHandler();
	[Signal] public delegate void LobbyOpenedEventHandler();
	[Signal] public delegate void LobbyFailedEventHandler(string reason);

	private readonly List<Lobby> _lobbies = new();
	private GodotObject _steam;

	public override void _Ready()
	{
		Instance = this;

		_steam = Engine.GetSingleton("Steam");
		if (_steam == null)
		{
			GD.Print("SteamManager: no Steam singleton; multiplayer stays on direct IP");
			return;
		}

		// True embeds Steam's callbacks in the engine loop, so nothing has to pump them.
		Godot.Collections.Dictionary result = _steam.Call("steamInitEx", AppId, true).AsGodotDictionary();
		IsAvailable = result["status"].AsInt32() == 0;
		if (!IsAvailable)
		{
			GD.Print($"SteamManager: Steam unavailable ({result["verbal"]}); multiplayer stays on direct IP");
			return;
		}

		GD.Print($"SteamManager: Steam ready as {PersonaName}");

		_steam.Connect("lobby_created", Callable.From<long, long>(OnLobbyCreated));
		_steam.Connect("lobby_joined", Callable.From<long, long, bool, long>(OnLobbyJoined));
		_steam.Connect("lobby_match_list", Callable.From<Godot.Collections.Array>(OnLobbyMatchList));
		// Accepting an invite or "Join game" while the game is already running.
		_steam.Connect("join_requested", Callable.From<long, long>((lobbyId, _) => JoinLobby((ulong)lobbyId)));

		NetworkManager.Instance.LeftServer += _ => LeaveLobby();
	}

	public string PersonaName => IsAvailable ? _steam.Call("getPersonaName").AsString() : "";

	public void HostLobby()
	{
		if (!IsAvailable) return;
		_steam.Call("createLobby", PublicLobby, NetworkManager.MaxPlayers);
	}

	public void JoinLobby(ulong lobbyId)
	{
		if (!IsAvailable) return;
		_steam.Call("joinLobby", lobbyId);
	}

	public void RefreshLobbies()
	{
		if (!IsAvailable) return;
		_steam.Call("addRequestLobbyListStringFilter", "game", GameTag, 0);
		_steam.Call("addRequestLobbyListDistanceFilter", Worldwide);
		_steam.Call("requestLobbyList");
	}

	// Steam's own invite dialog, so the host picks friends from their list.
	public void InviteFriends()
	{
		if (IsAvailable && LobbyId != 0) _steam.Call("activateGameOverlayInviteDialog", LobbyId);
	}

	public void LeaveLobby()
	{
		if (!IsAvailable || LobbyId == 0) return;
		_steam.Call("leaveLobby", LobbyId);
		LobbyId = 0;
	}

	private void OnLobbyCreated(long result, long lobbyId)
	{
		if (result != ResultOk)
		{
			EmitSignal(SignalName.LobbyFailed, "Steam could not create the lobby");
			return;
		}

		LobbyId = (ulong)lobbyId;
		_steam.Call("setLobbyData", lobbyId, "game", GameTag);
		_steam.Call("setLobbyData", lobbyId, "host", ConfigFileHandler.Instance.LoadPlayerName());

		NetworkManager.Instance.PeerFactory = new SteamPeerFactory(LobbyId);
		if (NetworkManager.Instance.Host())
		{
			EmitSignal(SignalName.LobbyOpened);
			return;
		}

		LeaveLobby();
		EmitSignal(SignalName.LobbyFailed, "could not host over Steam");
	}

	private void OnLobbyJoined(long lobbyId, long permissions, bool locked, long response)
	{
		// A host is a member of its own lobby, and gets this for the lobby it just opened.
		if (NetworkManager.Instance.IsActive) return;

		if (response != JoinedOk)
		{
			EmitSignal(SignalName.LobbyFailed, "Steam refused the lobby");
			return;
		}

		LobbyId = (ulong)lobbyId;
		NetworkManager.Instance.PeerFactory = new SteamPeerFactory(LobbyId);
		if (!NetworkManager.Instance.Join(""))
		{
			LeaveLobby();
			EmitSignal(SignalName.LobbyFailed, "could not reach the host over Steam");
		}
	}

	private void OnLobbyMatchList(Godot.Collections.Array lobbies)
	{
		_lobbies.Clear();
		foreach (Variant entry in lobbies)
		{
			long id = entry.AsInt64();
			_lobbies.Add(new Lobby((ulong)id,
				_steam.Call("getLobbyData", id, "host").AsString(),
				_steam.Call("getNumLobbyMembers", id).AsInt32()));
		}

		EmitSignal(SignalName.LobbiesFound);
	}
}
