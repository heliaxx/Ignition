using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class MultiplayerPanel : Control
{
	// What a host can set a match to.
	private static readonly int[] KillLimits = { 5, 10, 15, 25 };
	private static readonly int[] TimeLimits = { 5, 10, 15, 20 };

	private const double RefreshInterval = 5.0;

	private static readonly Color Gold = new(0.894118f, 0.717647f, 0.337255f);
	private static readonly Color Dim = new(0.55f, 0.55f, 0.55f);

	private enum Transport { Steam, Eos, Enet }

	// Lobbies that have been found, whatever transport layer they are on.
	private record Listing(Transport Transport, string Id, LobbyInfo Info)
	{
		public string Key => $"{Transport}:{Id}";
	}

	private readonly List<Transport> _hostTransports = new();
	private readonly List<Listing> _listings = new();
	private readonly ButtonGroup _rowGroup = new();
	private string _selectedKey;
	private AudioStreamPlayer _click;
	private double _sinceRefresh;

	private LineEdit _name;
	private OptionButton _transportOption;
	private Button _host;
	private CheckBox _inviteOnly;
	private LineEdit _address;
	private Button _joinIp;
	private Control _codeRow;
	private LineEdit _code;
	private Button _joinCode;
	private VBoxContainer _games;
	private Label _noGames;
	private Control _placeholder;
	private Control _info;
	private Label _hostName;
	private Label _type;
	private Label _state;
	private OptionButton _killLimit;
	private OptionButton _timeLimit;
	private VBoxContainer _players;
	private Button _joinLobby;
	private Button _ready;
	private Button _start;
	private Button _invite;
	private Control _friends;
	private VBoxContainer _onlineBox;
	private VBoxContainer _offlineBox;
	private LineEdit _friendsSearch;
	private Button _copyCode;
	private Button _leave;
	private Label _status;

	public override void _Ready()
	{
		_click = MenuUtils.AttachButtonSounds(this);

		_name = (LineEdit)FindChild("NameEdit");
		_name.Text = ConfigFileHandler.Instance.LoadPlayerName();
		// Saved as it is typed: the name has to be on disk before Host or Join reads it.
		_name.TextChanged += ConfigFileHandler.Instance.SavePlayerName;

		_transportOption = (OptionButton)FindChild("TransportOption");
		_host = (Button)FindChild("HostButton");
		_inviteOnly = (CheckBox)FindChild("InviteOnlyCheck");
		_address = (LineEdit)FindChild("AddressEdit");
		_joinIp = (Button)FindChild("JoinIpButton");
		_codeRow = (Control)FindChild("JoinCodeRow");
		_code = (LineEdit)FindChild("CodeEdit");
		_joinCode = (Button)FindChild("JoinCodeButton");
		_games = (VBoxContainer)FindChild("GamesBox");
		_noGames = (Label)FindChild("NoGamesLabel");
		_placeholder = (Control)FindChild("Placeholder");
		_info = (Control)FindChild("Info");
		_hostName = (Label)FindChild("HostName");
		_type = (Label)FindChild("TypeLabel");
		_state = (Label)FindChild("StateLabel");
		_killLimit = (OptionButton)FindChild("KillLimitOption");
		_timeLimit = (OptionButton)FindChild("TimeLimitOption");
		_players = (VBoxContainer)FindChild("PlayersBox");
		_joinLobby = (Button)FindChild("JoinLobbyButton");
		_ready = (Button)FindChild("ReadyButton");
		_start = (Button)FindChild("StartButton");
		_invite = (Button)FindChild("InviteButton");
		_friends = (Control)FindChild("Friends");
		_onlineBox = (VBoxContainer)FindChild("OnlineBox");
		_offlineBox = (VBoxContainer)FindChild("OfflineBox");
		_friendsSearch = (LineEdit)FindChild("FriendsSearch");
		_friendsSearch.TextChanged += FilterFriends;
		_copyCode = (Button)FindChild("CopyCodeButton");
		_copyCode.Pressed += () =>
		{
			DisplayServer.ClipboardSet(EosManager.Instance.LobbyCode);
			_status.Text = "code copied";
		};
		_leave = (Button)FindChild("LeaveButton");
		_status = (Label)FindChild("StatusLabel");

		FillLimits(_killLimit, KillLimits, "{0} KILLS");
		FillLimits(_timeLimit, TimeLimits, "{0} MIN");

		((Button)FindChild("RefreshButton")).Pressed += RefreshGames;
		((Button)FindChild("BackButton")).Pressed += OnBack;
		_host.Pressed += OnHost;
		_transportOption.ItemSelected += _ => Refresh();
		_joinIp.Pressed += OnJoinIp;
		_joinCode.Pressed += OnJoinCode;
		_code.TextSubmitted += _ => OnJoinCode();
		_joinLobby.Pressed += OnJoinLobby;
		_ready.Toggled += pressed => MatchManager.Instance.SetLocalReady(pressed);
		_start.Pressed += () => MatchManager.Instance.StartMatch();
		_invite.Pressed += ShowFriends;
		((Button)FindChild("FriendsBackButton")).Pressed += () =>
		{
			_friends.Visible = false;
			Refresh();
		};
		_leave.Pressed += OnLeave;
		_killLimit.ItemSelected += _ => OnLimitsChanged();
		_timeLimit.ItemSelected += _ => OnLimitsChanged();

		NetworkManager net = NetworkManager.Instance;
		net.PeerJoined += OnPeerJoined;
		net.PeerLeft += OnPeerLeft;
		net.JoinedServer += Refresh;
		net.LeftServer += OnLeftServer;
		net.SessionMoving += Refresh;
		net.SessionMoved += Refresh;
		MatchManager.Instance.LobbyChanged += Refresh;
		SteamManager.Instance.LobbiesFound += OnLobbiesFound;
		SteamManager.Instance.LobbyOpened += Refresh;
		SteamManager.Instance.LobbyFailed += OnLobbyFailed;
		EosManager.Instance.LobbiesFound += OnLobbiesFound;
		EosManager.Instance.LobbyOpened += Refresh;
		EosManager.Instance.LobbyFailed += OnLobbyFailed;
		// EOS logs in during startup, so it can arrive after this panel is open.
		EosManager.Instance.AvailabilityChanged += OnEosAvailable;

		BuildHostTransports();
		OnLobbiesFound();
		RefreshGames();
		// Why the last match ended, when this player did not choose to leave it.
		_status.Text = MatchManager.Instance.TakeLeaveReason() ?? "";
	}

	// NetworkManager and the others outlive this panel, so their signals must not keep
	// pointing here.
	public override void _ExitTree()
	{
		NetworkManager net = NetworkManager.Instance;
		if (net == null) return;

		net.PeerJoined -= OnPeerJoined;
		net.PeerLeft -= OnPeerLeft;
		net.JoinedServer -= Refresh;
		net.LeftServer -= OnLeftServer;
		net.SessionMoving -= Refresh;
		net.SessionMoved -= Refresh;
		MatchManager.Instance.LobbyChanged -= Refresh;
		SteamManager.Instance.LobbiesFound -= OnLobbiesFound;
		SteamManager.Instance.LobbyOpened -= Refresh;
		SteamManager.Instance.LobbyFailed -= OnLobbyFailed;
		EosManager.Instance.LobbiesFound -= OnLobbiesFound;
		EosManager.Instance.LobbyOpened -= Refresh;
		EosManager.Instance.LobbyFailed -= OnLobbyFailed;
		EosManager.Instance.AvailabilityChanged -= OnEosAvailable;
	}

	public override void _Process(double delta)
	{
		if (NetworkManager.Instance.IsActive || NetworkManager.Instance.IsMoving) return;

		_sinceRefresh += delta;
		if (_sinceRefresh >= RefreshInterval) RefreshGames();
	}

	private void RefreshGames()
	{
		_sinceRefresh = 0.0;
		SteamManager.Instance.RefreshLobbies();
		EosManager.Instance.RefreshLobbies();
	}

	private void OnEosAvailable()
	{
		BuildHostTransports();
		RefreshGames();
	}

	// Only what this machine can actually reach, so a transport is never offered in vain.
	private void BuildHostTransports()
	{
		Transport chosen = _hostTransports.Count > 0 ? ChosenHostTransport : Transport.Steam;
		_hostTransports.Clear();
		_transportOption.Clear();

		if (SteamManager.Instance.IsAvailable) AddHostTransport(Transport.Steam);
		if (EosManager.Instance.IsAvailable) AddHostTransport(Transport.Eos);
		AddHostTransport(Transport.Enet);

		int index = _hostTransports.IndexOf(chosen);
		_transportOption.Selected = index >= 0 ? index : 0;
	}

	private void AddHostTransport(Transport transport)
	{
		_transportOption.AddItem(TransportName(transport));
		_hostTransports.Add(transport);
	}

	private Transport ChosenHostTransport => _hostTransports[Mathf.Max(_transportOption.Selected, 0)];

	private static string TransportName(Transport transport) => transport switch
	{
		Transport.Steam => "STEAM",
		Transport.Eos => "EOS",
		_ => "ENET",
	};

	// Each manager keeps its last search, so both are merged whenever either answers.
	private void OnLobbiesFound()
	{
		_listings.Clear();
		foreach (SteamManager.Lobby lobby in SteamManager.Instance.Lobbies)
			_listings.Add(new Listing(Transport.Steam, lobby.Id.ToString(), lobby.Info));
		foreach (EosManager.Lobby lobby in EosManager.Instance.Lobbies)
			_listings.Add(new Listing(Transport.Eos, lobby.Id, lobby.Info));

		BuildGames();
		Refresh();
	}

	private void OnHost()
	{
		_status.Text = "";
		switch (ChosenHostTransport)
		{
			case Transport.Steam:
				SteamManager.Instance.HostLobby(_inviteOnly.ButtonPressed);
				_status.Text = "opening a Steam lobby…";
				break;
			case Transport.Eos:
				EosManager.Instance.HostLobby(_inviteOnly.ButtonPressed);
				_status.Text = "opening an EOS lobby…";
				break;
			default:
				// A Steam or EOS session earlier may have left its own transport in place.
				NetworkManager.Instance.PeerFactory = new EnetPeerFactory();
				if (NetworkManager.Instance.Host()) Refresh();
				else _status.Text = "could not host";
				break;
		}
	}

	private void OnJoinIp()
	{
		string address = _address.Text.Length > 0 ? _address.Text : "127.0.0.1";
		NetworkManager.Instance.PeerFactory = new EnetPeerFactory();
		_status.Text = NetworkManager.Instance.Join(address)
			? $"connecting to {address}…"
			: $"could not reach {address}";
	}

	private void OnJoinCode()
	{
		if (_code.Text.StripEdges().Length == 0 || NetworkManager.Instance.IsActive) return;
		EosManager.Instance.JoinByCode(_code.Text);
		_status.Text = "looking for that game…";
	}

	private void OnJoinLobby()
	{
		Listing listing = Selected;
		if (listing == null) return;

		if (listing.Transport == Transport.Steam)
			SteamManager.Instance.JoinLobby(ulong.Parse(listing.Id));
		else
			EosManager.Instance.JoinLobby(listing.Id);
		_status.Text = $"joining {listing.Info.Host}…";
	}

	private void OnLeave()
	{
		NetworkManager.Instance.Leave();
		SteamManager.Instance.LeaveLobby();
		EosManager.Instance.LeaveLobby();
		_status.Text = "";
		Refresh();
		RefreshGames();
	}

	private void OnBack()
	{
		OnLeave();
		GetParent<MenuStack>().Pop();
	}

	private void OnLimitsChanged()
	{
		if (_killLimit.Selected < 0 || _timeLimit.Selected < 0) return;
		MatchManager.Instance.SetLimits(KillLimits[_killLimit.Selected], TimeLimits[_timeLimit.Selected]);
	}

	private void OnLobbyFailed(string reason)
	{
		Refresh();
		_status.Text = reason;
	}

	private void OnPeerJoined(int peerId) => Refresh();
	private void OnPeerLeft(int peerId) => Refresh();

	private void OnLeftServer(string reason)
	{
		Refresh();
		_status.Text = reason;
	}

	private Listing Selected => _listings.FirstOrDefault(l => l.Key == _selectedKey);

	private void Refresh()
	{
		NetworkManager net = NetworkManager.Instance;
		MatchManager match = MatchManager.Instance;
		// Everyone is briefly off while they reconnect, so the last view stays up meanwhile.
		if (net.IsMoving)
		{
			_status.Text = "switching to the new host…";
			return;
		}

		bool connected = net.IsActive;
		bool hosting = connected && net.IsServer;

		// Opening a session is for the browser; once in one, the right side is the lobby.
		_name.Editable = !connected;
		_transportOption.Disabled = connected;
		_host.Disabled = connected;
		// Direct IP is never listed, so it is invite-only already.
		_inviteOnly.Disabled = connected || _hostTransports.Count == 0 || ChosenHostTransport == Transport.Enet;
		_address.Editable = !connected;
		_joinIp.Disabled = connected;
		_codeRow.Visible = EosManager.Instance.IsAvailable;
		_code.Editable = !connected;
		_joinCode.Disabled = connected;
		foreach (Button row in _games.GetChildren().OfType<Button>())
		{
			row.Disabled = connected;
			MenuUtils.PaintRow(row);
		}
		_noGames.Visible = _listings.Count == 0;

		Listing listing = connected ? null : Selected;
		LobbyInfo info = connected ? LobbyInfo.Current() : listing?.Info;
		_friends.Visible &= connected && SteamManager.Instance.LobbyId != 0;
		_placeholder.Visible = info == null;
		_info.Visible = info != null && !_friends.Visible;
		if (info == null) return;

		_hostName.Text = info.Host;
		_type.Text = connected ? SessionTransportName() : TransportName(listing.Transport);
		if (info.InviteOnly) _type.Text += $"  CODE {info.Code}";
		_copyCode.Visible = connected && info.InviteOnly;
		_state.Text = info.InMatch ? "IN MATCH" : "IN LOBBY";
		_killLimit.Selected = Array.IndexOf(KillLimits, info.KillLimit);
		_timeLimit.Selected = Array.IndexOf(TimeLimits, info.TimeLimitMinutes);
		_killLimit.Disabled = !hosting;
		_timeLimit.Disabled = !hosting;
		BuildPlayers(info, hosting);

		bool full = info.Members.Count >= NetworkManager.MaxPlayers;
		_joinLobby.Visible = !connected;
		_joinLobby.Disabled = info.InMatch || full;
		_joinLobby.Text = info.InMatch ? "IN MATCH" : full ? "FULL" : "JOIN";
		// Only the host starts a match; only the others have anything to declare.
		_ready.Visible = connected && !hosting;
		_ready.SetPressedNoSignal(match.IsReady(net.LocalPeerId));
		_start.Visible = hosting;
		_start.Disabled = !match.CanStart;
		// Only Steam knows the player's friends; an EOS device login has none to invite.
		// Any player can invite, not just the host.
		_invite.Visible = connected && SteamManager.Instance.LobbyId != 0;
		_leave.Visible = connected;
		if (connected) _status.Text = Status(net, match);
	}

	private static string SessionTransportName() =>
		SteamManager.Instance.LobbyId != 0 ? "STEAM"
		: EosManager.Instance.InLobby ? "EOS"
		: "ENET";

	private static string Status(NetworkManager net, MatchManager match)
	{
		if (!net.IsServer) return "in lobby";
		if (net.Peers.Count < 2) return "waiting for another player";
		return match.CanStart ? "ready to start" : "waiting for everyone to ready up";
	}

	private void BuildGames()
	{
		// Only the rows: the "no games" line lives in the same list.
		foreach (Button row in _games.GetChildren().OfType<Button>())
			row.QueueFree();

		// Joinable lobbies first.
		foreach (Listing listing in _listings.OrderBy(l => l.Info.InMatch).ThenBy(l => l.Info.Host))
		{
			var cells = new List<Label> { MenuUtils.Cell(listing.Info.Host.ToUpperInvariant(), Colors.White, expand: true) };
			if (listing.Info.InMatch) cells.Add(MenuUtils.Cell("IN MATCH", Dim));
			cells.Add(MenuUtils.Cell(TransportName(listing.Transport), Colors.White));
			cells.Add(MenuUtils.Cell($"{listing.Info.Members.Count}/{NetworkManager.MaxPlayers}", Colors.White));

			Button row = MenuUtils.ListRow(_rowGroup, listing.Key == _selectedKey, cells.ToArray());
			string key = listing.Key;
			row.Pressed += () =>
			{
				_selectedKey = key;
				Refresh();
			};
			MenuUtils.WireControls(row, _click);
			_games.AddChild(row);
		}
	}

	private void BuildPlayers(LobbyInfo info, bool hosting)
	{
		foreach (Node row in _players.GetChildren())
			row.QueueFree();

		bool canMove = hosting && !info.InMatch && SessionTransportName() != "ENET";

		foreach (LobbyInfo.Member member in info.Members)
		{
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 12);
			row.AddChild(MenuUtils.Cell(member.Name, Colors.White, expand: true));
			row.AddChild(MenuUtils.Cell(member.State switch
			{
				LobbyInfo.MemberState.Host => "HOST",
				LobbyInfo.MemberState.Ready => "READY",
				_ => "NOT READY",
			}, member.State == LobbyInfo.MemberState.Waiting ? Dim : Gold));

			if (hosting && member.State != LobbyInfo.MemberState.Host)
			{
				int peerId = member.PeerId;
				string name = member.Name;
				if (canMove) row.AddChild(MemberAction("HOST", () => OnTransferHost(peerId, name)));
				row.AddChild(MemberAction("KICK", () => OnRemove(peerId, name, ban: false)));
				row.AddChild(MemberAction("BAN", () => OnRemove(peerId, name, ban: true)));
			}
			MenuUtils.WireControls(row, _click);
			_players.AddChild(row);
		}
	}

	private static Button MemberAction(string text, Action pressed)
	{
		var button = new Button { Text = text };
		button.Pressed += pressed;
		return button;
	}

	private void ShowFriends()
	{
		List<SteamManager.Friend> friends = SteamManager.Instance.Friends();
		FillFriends(_onlineBox, friends.Where(f => f.Online), "no friends online");
		FillFriends(_offlineBox, friends.Where(f => !f.Online), "nobody offline");
		_friendsSearch.Text = "";
		FilterFriends("");
		_friends.Visible = true;
		Refresh();
	}

	private const string FriendNameMeta = "friend_name";

	private void FillFriends(VBoxContainer box, IEnumerable<SteamManager.Friend> friends, string empty)
	{
		foreach (Node row in box.GetChildren())
			row.QueueFree();

		// Shown when no row is: the list is empty, or the search left nothing in it.
		box.AddChild(MenuUtils.Cell(friends.Any() ? "no matches" : empty, Dim));

		foreach (SteamManager.Friend friend in friends)
		{
			var cells = new List<Label> { MenuUtils.Cell(friend.Name, friend.Online ? Colors.White : Dim, expand: true) };
			if (friend.InThisGame) cells.Add(MenuUtils.Cell("IN GAME", Gold));
			Label status = MenuUtils.Cell("INVITE", Gold);
			cells.Add(status);

			// The whole row is the button, like the scenario and lobby lists; one invite per opening.
			Button row = MenuUtils.ListRow(null, false, cells.ToArray());
			long id = friend.Id;
			row.Pressed += () =>
			{
				status.Text = SteamManager.Instance.InviteToLobby(id) ? "SENT" : "FAILED";
				row.Disabled = true;
				MenuUtils.PaintRow(row);
			};
			row.SetMeta(FriendNameMeta, friend.Name);
			MenuUtils.WireControls(row, _click);
			box.AddChild(row);
		}
	}

	private void FilterFriends(string text)
	{
		string query = text.StripEdges();
		foreach (VBoxContainer box in new[] { _onlineBox, _offlineBox })
		{
			List<Node> live = box.GetChildren().Where(n => !n.IsQueuedForDeletion()).ToList();
			bool any = false;
			foreach (Button row in live.OfType<Button>())
			{
				row.Visible = row.GetMeta(FriendNameMeta).AsString().Contains(query, StringComparison.OrdinalIgnoreCase);
				any |= row.Visible;
			}
			live.OfType<Label>().First().Visible = !any;
		}
	}

	private void OnRemove(int peerId, string name, bool ban)
	{
		NetworkManager.Instance.Kick(peerId, ban);
		_status.Text = ban ? $"{name} is banned from this lobby" : $"{name} was kicked";
	}

	private void OnTransferHost(int peerId, string name)
	{
		if (SteamManager.Instance.LobbyId != 0) SteamManager.Instance.TransferHost(peerId);
		else if (EosManager.Instance.InLobby) EosManager.Instance.TransferHost(peerId);
		_status.Text = $"handing hosting to {name}…";
	}

	private static void FillLimits(OptionButton option, int[] values, string format)
	{
		foreach (int value in values)
			option.AddItem(string.Format(format, value), value);
	}
}
