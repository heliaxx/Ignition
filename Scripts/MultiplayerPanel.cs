using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

// Host/join bench for every transport this machine can reach, and the lobby it turns into once
// connected: who is here, what the host set the match to, and who has readied up.
public partial class MultiplayerPanel : Control
{
	// The server always holds peer id 1.
	private const int ServerPeerId = 1;

	// What a host can set a match to.
	private static readonly int[] KillLimits = { 5, 10, 15, 25 };
	private static readonly int[] TimeLimits = { 5, 10, 15, 20 };

	// Players on different transports cannot meet, so one is chosen before hosting or joining.
	private enum Transport { Steam, Eos, Enet }

	private readonly List<Transport> _transports = new();

	private OptionButton _transportOption;
	private LineEdit _name;
	private LineEdit _address;
	private Label _status;
	private Button _host;
	private Button _join;
	private Button _start;
	private Button _ready;
	private Label _gamesTitle;
	private VBoxContainer _games;
	private Button _refreshGames;
	private Button _invite;
	private Label _playersTitle;
	private VBoxContainer _players;
	private Label _mode;
	private Control _killRow;
	private Control _timeRow;
	private OptionButton _killLimit;
	private OptionButton _timeLimit;

	public override void _Ready()
	{
		MenuUtils.AttachButtonSounds(this);

		_name = (LineEdit)FindChild("NameEdit");
		_name.Text = ConfigFileHandler.Instance.LoadPlayerName();
		// Saved as it is typed: the name has to be on disk before Host or Join reads it.
		_name.TextChanged += ConfigFileHandler.Instance.SavePlayerName;

		_transportOption = (OptionButton)FindChild("TransportOption");
		_address = (LineEdit)FindChild("AddressEdit");
		_status = (Label)FindChild("StatusLabel");

		_host = (Button)FindChild("HostButton");
		_join = (Button)FindChild("JoinButton");
		_start = (Button)FindChild("StartButton");
		_ready = (Button)FindChild("ReadyButton");
		_gamesTitle = (Label)FindChild("GamesTitle");
		_games = (VBoxContainer)FindChild("GamesBox");
		_refreshGames = (Button)FindChild("RefreshButton");
		_invite = (Button)FindChild("InviteButton");
		_playersTitle = (Label)FindChild("PlayersTitle");
		_players = (VBoxContainer)FindChild("PlayersBox");
		_mode = (Label)FindChild("ModeLabel");
		_killRow = (Control)FindChild("KillRow");
		_timeRow = (Control)FindChild("TimeRow");
		_killLimit = (OptionButton)FindChild("KillLimitOption");
		_timeLimit = (OptionButton)FindChild("TimeLimitOption");

		FillLimits(_killLimit, KillLimits, "{0} KILLS");
		FillLimits(_timeLimit, TimeLimits, "{0} MIN");

		_host.Pressed += OnHost;
		_refreshGames.Pressed += RefreshGames;
		_invite.Pressed += SteamManager.Instance.InviteFriends;
		_transportOption.ItemSelected += _ => { RefreshGames(); Refresh(); };
		_join.Pressed += OnJoin;
		_start.Pressed += OnStart;
		_ready.Toggled += pressed => MatchManager.Instance.SetLocalReady(pressed);
		_killLimit.ItemSelected += _ => OnLimitsChanged();
		_timeLimit.ItemSelected += _ => OnLimitsChanged();
		((Button)FindChild("BackButton")).Pressed += OnBack;

		NetworkManager net = NetworkManager.Instance;
		net.PeerJoined += OnPeerJoined;
		net.PeerLeft += OnPeerLeft;
		net.JoinedServer += OnJoinedServer;
		net.LeftServer += OnLeftServer;
		MatchManager.Instance.LobbyChanged += Refresh;
		SteamManager.Instance.LobbiesFound += Refresh;
		SteamManager.Instance.LobbyOpened += Refresh;
		SteamManager.Instance.LobbyFailed += OnLobbyFailed;
		EosManager.Instance.LobbiesFound += Refresh;
		EosManager.Instance.LobbyOpened += Refresh;
		EosManager.Instance.LobbyFailed += OnLobbyFailed;
		// EOS logs in during startup, so it can arrive after this panel is open.
		EosManager.Instance.AvailabilityChanged += BuildTransports;

		BuildTransports();
		// Why the last match ended, when this player did not choose to leave it.
		if (MatchManager.Instance.TakeLeaveReason() is string reason)
			_status.Text = reason;
	}

	// NetworkManager and MatchManager outlive this panel, so their signals must not keep
	// pointing here.
	public override void _ExitTree()
	{
		NetworkManager net = NetworkManager.Instance;
		if (net == null) return;

		net.PeerJoined -= OnPeerJoined;
		net.PeerLeft -= OnPeerLeft;
		net.JoinedServer -= OnJoinedServer;
		net.LeftServer -= OnLeftServer;
		MatchManager.Instance.LobbyChanged -= Refresh;
		SteamManager.Instance.LobbiesFound -= Refresh;
		SteamManager.Instance.LobbyOpened -= Refresh;
		SteamManager.Instance.LobbyFailed -= OnLobbyFailed;
		EosManager.Instance.LobbiesFound -= Refresh;
		EosManager.Instance.LobbyOpened -= Refresh;
		EosManager.Instance.LobbyFailed -= OnLobbyFailed;
		EosManager.Instance.AvailabilityChanged -= BuildTransports;
	}

	private void BuildTransports()
	{
		Transport chosen = _transports.Count > 0 ? Chosen : Transport.Steam;
		_transports.Clear();
		_transportOption.Clear();

		if (SteamManager.Instance.IsAvailable) AddTransport(Transport.Steam, "STEAM");
		if (EosManager.Instance.IsAvailable) AddTransport(Transport.Eos, "EOS");
		AddTransport(Transport.Enet, "ENET");

		int index = _transports.IndexOf(chosen);
		_transportOption.Selected = index >= 0 ? index : 0;
		RefreshGames();
		Refresh();
	}

	private void AddTransport(Transport transport, string label)
	{
		_transportOption.AddItem(label);
		_transports.Add(transport);
	}

	private Transport Chosen => _transports[Mathf.Max(_transportOption.Selected, 0)];

	private void RefreshGames()
	{
		if (Chosen == Transport.Steam) SteamManager.Instance.RefreshLobbies();
		else if (Chosen == Transport.Eos) EosManager.Instance.RefreshLobbies();
	}

	private void OnHost()
	{
		switch (Chosen)
		{
			case Transport.Steam:
				SteamManager.Instance.HostLobby();
				_status.Text = "opening a Steam lobby…";
				break;
			case Transport.Eos:
				EosManager.Instance.HostLobby();
				_status.Text = "opening an EOS lobby…";
				break;
			default:
				if (NetworkManager.Instance.Host()) Refresh();
				else _status.Text = "could not host";
				break;
		}
	}

	private void OnLobbyFailed(string reason)
	{
		Refresh();
		_status.Text = reason;
	}

	private void OnJoin()
	{
		string address = _address.Text.Length > 0 ? _address.Text : "127.0.0.1";
		if (NetworkManager.Instance.Join(address))
			_status.Text = $"connecting to {address}…";
		else
			_status.Text = $"could not reach {address}";
	}

	private void OnStart()
	{
		MatchManager.Instance.StartMatch();
	}

	private void OnLimitsChanged()
	{
		MatchManager.Instance.SetLimits(KillLimits[_killLimit.Selected], TimeLimits[_timeLimit.Selected]);
	}

	private void OnBack()
	{
		NetworkManager.Instance.Leave();
		SteamManager.Instance.LeaveLobby();
		EosManager.Instance.LeaveLobby();
		GetParent<MenuStack>().Pop();
	}

	private void OnPeerJoined(int peerId) => Refresh();
	private void OnPeerLeft(int peerId) => Refresh();
	private void OnJoinedServer() => Refresh();
	private void OnLeftServer(string reason) { Refresh(); _status.Text = reason; }

	private void Refresh()
	{
		NetworkManager net = NetworkManager.Instance;
		MatchManager match = MatchManager.Instance;
		bool connected = net.IsActive;
		bool hosting = connected && net.IsServer;
		bool browsing = !connected && Chosen != Transport.Enet;

		// Offline this is where a session is opened; connected, it is the lobby.
		_name.Editable = !connected;
		_host.Visible = !connected;
		_transportOption.Visible = !connected;
		_join.Visible = !connected && !browsing;
		_address.Visible = !connected && !browsing;
		_gamesTitle.Visible = browsing;
		_gamesTitle.Text = Chosen == Transport.Eos ? "EOS GAMES" : "STEAM GAMES";
		_games.Visible = browsing;
		_refreshGames.Visible = browsing;
		// Only Steam knows the player's friends; an EOS device login has none to invite.
		_invite.Visible = hosting && SteamManager.Instance.LobbyId != 0;
		_playersTitle.Visible = connected;
		_players.Visible = connected;
		_mode.Visible = connected;
		_killRow.Visible = connected;
		_timeRow.Visible = connected;
		// Only the host starts a match; only the others have anything to declare.
		_start.Visible = hosting;
		_ready.Visible = connected && !hosting;

		if (!connected)
		{
			BuildGames();
			_status.Text = "offline";
			return;
		}

		BuildPlayers();
		Select(_killLimit, KillLimits, match.KillLimit);
		Select(_timeLimit, TimeLimits, match.TimeLimitMinutes);
		_killLimit.Disabled = !hosting;
		_timeLimit.Disabled = !hosting;
		_start.Disabled = !match.CanStart;
		_ready.SetPressedNoSignal(match.IsReady(net.LocalPeerId));
		_status.Text = Status(net, match);
	}

	private static string Status(NetworkManager net, MatchManager match)
	{
		if (!net.IsServer) return "in lobby";
		if (net.Peers.Count < 2) return "waiting for another player";
		return match.CanStart ? "ready to start" : "waiting for everyone to ready up";
	}

	private void BuildGames()
	{
		foreach (Node row in _games.GetChildren())
			row.QueueFree();

		if (Chosen == Transport.Steam)
			foreach (SteamManager.Lobby lobby in SteamManager.Instance.Lobbies)
				AddGame(lobby.Host, lobby.Players, () => SteamManager.Instance.JoinLobby(lobby.Id));
		else if (Chosen == Transport.Eos)
			foreach (EosManager.Lobby lobby in EosManager.Instance.Lobbies)
				AddGame(lobby.Host, lobby.Players, () => EosManager.Instance.JoinLobby(lobby.Id));
	}

	private void AddGame(string host, int players, Action join)
	{
		var row = new Button { Text = $"{host}   {players}/{NetworkManager.MaxPlayers}" };
		row.Pressed += join;
		_games.AddChild(row);
	}

	private void BuildPlayers()
	{
		foreach (Node row in _players.GetChildren())
			row.QueueFree();

		foreach (int peerId in NetworkManager.Instance.Peers.OrderBy(id => id))
		{
			string state = peerId == ServerPeerId ? "HOST"
				: MatchManager.Instance.IsReady(peerId) ? "READY"
				: "…";
			_players.AddChild(new Label { Text = $"{NetworkManager.Instance.NameOf(peerId)}   {state}" });
		}
	}

	private static void FillLimits(OptionButton option, int[] values, string format)
	{
		foreach (int value in values)
			option.AddItem(string.Format(format, value), value);
	}

	private static void Select(OptionButton option, int[] values, int value)
	{
		int index = Array.IndexOf(values, value);
		if (index >= 0) option.Selected = index;
	}
}
