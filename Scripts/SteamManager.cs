using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

// Steam side of multiplayer: starts Steam, owns the lobby a session runs in, and points
// NetworkManager at the Steam transport. Carries no RPCs; it only opens the connection.
// GodotSteam is a GDExtension with no C# bindings, hence the calls by name.
public partial class SteamManager : Node
{
	// Valve's test app. Every game using it shares one lobby pool, hence the tag filter.
	private const int AppId = 480;
	private const string GameTag = "heliaxx_ignition";

	// Steam's lobby types and its "everything went fine" results. A private lobby is left out
	// of searches and joined only through an invite.
	private const int PrivateLobby = 0;
	private const int PublicLobby = 2;

	// Steam's flag for an ordinary friend, and the persona state of one who is offline.
	private const int RegularFriends = 4;
	private const int PersonaOffline = 0;

	// Steam otherwise prefers nearby lobbies and hides the rest of the world's.
	private const int Worldwide = 3;
	private const int ResultOk = 1;
	private const int JoinedOk = 1;

	public record Lobby(ulong Id, LobbyInfo Info);

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

	private string _published = "";

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

		NetworkManager.Instance.SessionClosed += LeaveLobby;
	}

	public string PersonaName => IsAvailable ? _steam.Call("getPersonaName").AsString() : "";

	public override void _Process(double delta)
	{
		if (LobbyId == 0 || !NetworkManager.Instance.IsActive || !NetworkManager.Instance.IsServer) return;

		Dictionary<string, string> data = LobbyInfo.Current().ToData();
		string snapshot = string.Join("\n", data.Values);
		if (snapshot == _published) return;

		_published = snapshot;
		foreach (var (key, value) in data)
			_steam.Call("setLobbyData", LobbyId, key, value);
	}

	public void HostLobby(bool inviteOnly)
	{
		if (!IsAvailable) return;
		_steam.Call("createLobby", inviteOnly ? PrivateLobby : PublicLobby, NetworkManager.MaxPlayers);
	}

	public void TransferHost(int peerId)
	{
		string steamId = NetworkManager.Instance.IdentityOf(peerId);
		if (LobbyId == 0 || !long.TryParse(steamId, out long id)
			|| !_steam.Call("setLobbyOwner", LobbyId, id).AsBool())
		{
			EmitSignal(SignalName.LobbyFailed, "Steam would not hand the lobby over");
			return;
		}
		NetworkManager.Instance.MoveSession(peerId, steamId);
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

	public record Friend(long Id, string Name, bool Online, bool InThisGame);

	// All friends are returned, even those who are offline or in another game. 
	// The list is empty if Steam is unavailable.
	public List<Friend> Friends()
	{
		var friends = new List<Friend>();
		if (!IsAvailable) return friends;

		int count = _steam.Call("getFriendCount", RegularFriends).AsInt32();
		for (int i = 0; i < count; i++)
		{
			long id = _steam.Call("getFriendByIndex", i, RegularFriends).AsInt64();
			bool online = _steam.Call("getFriendPersonaState", id).AsInt32() != PersonaOffline;
			Godot.Collections.Dictionary game = _steam.Call("getFriendGamePlayed", id).AsGodotDictionary();
			bool inThisGame = game.ContainsKey("id") && game["id"].AsInt64() == AppId;
			friends.Add(new Friend(id, _steam.Call("getFriendPersonaName", id).AsString(), online, inThisGame));
		}
		return friends.OrderByDescending(f => f.InThisGame).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
	}

	// Arrives as a Steam chat invite; accepting it joins through join_requested.
	public bool InviteToLobby(long friendId) =>
		IsAvailable && LobbyId != 0 && _steam.Call("inviteUserToLobby", LobbyId, friendId).AsBool();

	public void LeaveLobby()
	{
		if (!IsAvailable || LobbyId == 0) return;
		_steam.Call("leaveLobby", LobbyId);
		LobbyId = 0;
		_published = "";
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
			LobbyInfo info = LobbyInfo.FromData(key => _steam.Call("getLobbyData", id, key).AsString());
			if (info != null) _lobbies.Add(new Lobby((ulong)id, info));
		}

		EmitSignal(SignalName.LobbiesFound);
	}
}
