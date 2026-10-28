using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

// What a death means for the participant: back shortly, out for good, or the match itself
// is already over and something else is showing the result.
public enum DeathOutcome { Respawning, RunOver, MatchOver }

// Owns match lifecycle: which level everyone loads and who spawns where. Sits above
// NetworkManager, which knows only about connections and nothing about a match.
public partial class MatchManager : Node
{
	public const string DeathmatchLevel = "res://Scenes/LevelDeathmatch.tscn";
	public const string MenuScene = "res://Scenes/Menu.tscn";

	public static MatchManager Instance { get; private set; }

	// Peer order fixed by the server when the match starts; a peer's position in it picks its
	// start from the level's shuffled spawn points. Every peer gets the same array, so spawn
	// assignment needs no further agreement.
	private int[] _spawnOrder = Array.Empty<int>();

	// Picked by the server and handed to everyone: the asteroid field derives every rock's
	// position and id from it, so without a shared seed no two machines see the same arena.
	public long WorldSeed { get; private set; } = 1;

	// Also the server's pick: the level shuffles its spawn points with it, the same way on
	// every machine.
	public long SpawnSeed { get; private set; }

	// Lobby state, owned by the server and mirrored to everyone: who has readied up and what
	// the host set the limits to.
	private readonly HashSet<int> _ready = new();

	public int KillLimit { get; private set; } = DefaultKillLimit;
	public int TimeLimitMinutes { get; private set; } = DefaultTimeLimitMinutes;
	public MatchMode Mode { get; private set; } = MatchMode.Deathmatch;

	// Lobby sides, mirrored like readiness. The server puts each arrival on the smaller side and
	// the host can move anyone to the other side.
	private readonly Dictionary<int, int> _lobbyTeams = new();

	public int LobbyTeamOf(int peerId) => _lobbyTeams.TryGetValue(peerId, out int team) ? team : Teams.None;

	[Signal] public delegate void LobbyChangedEventHandler();

	public bool IsReady(int peerId) => _ready.Contains(peerId);

	// Advertised to the lobby browser, which cannot join a match already running.
	public bool InMatch => GetTree().CurrentScene is LevelDeathmatch;

	// Two ships make a match, and the host waits until the others say they are ready. 
	// TDM needs somebody on each side.
	public bool CanStart => !NetworkManager.Instance.IsActive
		|| (NetworkManager.Instance.Peers.Count >= 2
			&& NetworkManager.Instance.Peers.All(id => id == ServerPeerId || _ready.Contains(id))
			&& (Mode != MatchMode.TeamDeathmatch || BothSidesManned));

	public bool BothSidesManned => Teams.Both.All(team => NetworkManager.Instance.Peers.Any(id => LobbyTeamOf(id) == team));

	public override void _Ready()
	{
		Instance = this;
		NetworkManager.Instance.PeerJoined += OnPeerJoined;
		NetworkManager.Instance.PeerLeft += OnPeerLeft;
		NetworkManager.Instance.LeftServer += OnLeftServer;
		// Everyone comes back under a new peer id, readies up again for the new host and is put
		// back on a side by it.
		NetworkManager.Instance.SessionMoving += () =>
		{
			_ready.Clear();
			_lobbyTeams.Clear();
			EmitSignal(SignalName.LobbyChanged);
		};
	}

	// Client -> server. The server stamps the sender, so nobody readies up for somebody else.
	public void SetLocalReady(bool ready)
	{
		if (!NetworkManager.Instance.IsConnected) return;

		if (NetworkManager.Instance.IsServer)
			ApplyReady(NetworkManager.Instance.LocalPeerId, ready);
		else
			RpcId(ServerPeerId, MethodName.RequestReady, ready);
	}

	// Host only; on anyone else the server simply never hears about it.
	public void SetLimits(int kills, int minutes)
	{
		if (!NetworkManager.Instance.IsServer) return;

		KillLimit = kills;
		TimeLimitMinutes = minutes;
		BroadcastLobby();
	}

	// Host only, like the limits.
	public void SetMode(MatchMode mode)
	{
		if (!NetworkManager.Instance.IsServer) return;

		Mode = mode;
		BroadcastLobby();
	}

	// Host only: each player goes to the side they are not on, so picks from both sides swap.
	public void SwitchTeams(IEnumerable<int> peerIds)
	{
		if (!NetworkManager.Instance.IsServer) return;

		foreach (int peerId in peerIds)
			if (_lobbyTeams.TryGetValue(peerId, out int team))
				_lobbyTeams[peerId] = Teams.Other(team);
		BroadcastLobby();
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestReady(bool ready) => ApplyReady(Multiplayer.GetRemoteSenderId(), ready);

	private void ApplyReady(int peerId, bool ready)
	{
		if (ready) _ready.Add(peerId);
		else _ready.Remove(peerId);
		BroadcastLobby();
	}

	private void BroadcastLobby()
	{
		if (!NetworkManager.Instance.IsActive)
		{
			EmitSignal(SignalName.LobbyChanged);
			return;
		}

		AssignTeams();
		int[] teamPeers = _lobbyTeams.Keys.ToArray();
		Rpc(MethodName.SyncLobby, _ready.ToArray(), KillLimit, TimeLimitMinutes, (int)Mode,
			teamPeers, teamPeers.Select(id => _lobbyTeams[id]).ToArray());
	}

	// Server: whoever left gives up their side, and whoever has none joins the smaller one.
	private void AssignTeams()
	{
		foreach (int gone in _lobbyTeams.Keys.Where(id => !NetworkManager.Instance.Peers.Contains(id)).ToList())
			_lobbyTeams.Remove(gone);

		foreach (int peerId in NetworkManager.Instance.Peers.OrderBy(id => id))
		{
			if (_lobbyTeams.ContainsKey(peerId)) continue;
			int ones = _lobbyTeams.Values.Count(team => team == Teams.One);
			_lobbyTeams[peerId] = ones <= _lobbyTeams.Count - ones ? Teams.One : Teams.Two;
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SyncLobby(int[] ready, int killLimit, int timeLimitMinutes, int mode, int[] teamPeers, int[] teamSides)
	{
		_ready.Clear();
		foreach (int peerId in ready) _ready.Add(peerId);
		KillLimit = killLimit;
		TimeLimitMinutes = timeLimitMinutes;
		Mode = (MatchMode)mode;
		_lobbyTeams.Clear();
		for (int i = 0; i < teamPeers.Length; i++) _lobbyTeams[teamPeers[i]] = teamSides[i];
		EmitSignal(SignalName.LobbyChanged);
	}

	// A new arrival knows nothing about the lobby until the server tells it.
	private void OnPeerJoined(int peerId)
	{
		if (NetworkManager.Instance.IsServer) BroadcastLobby();
	}

	public void StartMatch(string levelPath = DeathmatchLevel)
	{
		if (!NetworkManager.Instance.IsServer || !CanStart) return;

		long seed = (long)GD.Randi() << 32 | GD.Randi();
		long spawnSeed = (long)GD.Randi() << 32 | GD.Randi();

		// Offline the same path runs without a peer to send to.
		if (!NetworkManager.Instance.IsActive)
		{
			BeginMatch(levelPath, new[] { NetworkManager.Instance.LocalPeerId }, seed, spawnSeed, KillLimit, TimeLimitMinutes,
				(int)MatchMode.Deathmatch, new[] { Teams.None });
			return;
		}

		int[] order = NetworkManager.Instance.Peers.OrderBy(id => id).ToArray();
		Rpc(MethodName.BeginMatch, levelPath, order, seed, spawnSeed, KillLimit, TimeLimitMinutes, (int)Mode,
			order.Select(LobbyTeamOf).ToArray());
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void BeginMatch(string levelPath, int[] spawnOrder, long worldSeed, long spawnSeed,
		int killLimit, int timeLimitMinutes, int mode, int[] teams)
	{
		_spawnOrder = spawnOrder;
		Mode = (MatchMode)mode;
		_matchTeams.Clear();
		for (int i = 0; i < spawnOrder.Length; i++) _matchTeams[spawnOrder[i]] = teams[i];
		WorldSeed = worldSeed;
		SpawnSeed = spawnSeed;
		KillLimit = killLimit;
		TimeLimitMinutes = timeLimitMinutes;
		_ready.Clear();
		_matchOver = false;
		_loaded.Clear();
		InPlay = false;
		_loadWaitLeft = MaxLoadWait;
		_timeLeft = timeLimitMinutes * 60.0;
		GetTree().ChangeSceneToFile(levelPath);
	}

	// The server tracks who has loaded in, and starts the match once everyone is ready or the wait expires.
	private readonly HashSet<int> _loaded = new();

	// Set on every peer once the server has heard from the whole roster, or gave up waiting.
	public bool InPlay { get; private set; }

	// Time after which the server starts the match even if some peers have not loaded in yet.
	private const double MaxLoadWait = 30.0;

	// Counted down on every peer for the HUD.
	// Only the server's expiry starts the match.
	private double _loadWaitLeft;

	public double LoadWaitRemaining => _loadWaitLeft;

	[Signal] public delegate void PlayStartedEventHandler();

	// Called by the level once it is on screen.
	public void ReportLoaded()
	{
		if (!NetworkManager.Instance.IsActive)
			StartPlay();
		else if (NetworkManager.Instance.IsServer)
			ApplyLoaded(NetworkManager.Instance.LocalPeerId);
		else
			RpcId(ServerPeerId, MethodName.RequestLoaded);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestLoaded() => ApplyLoaded(Multiplayer.GetRemoteSenderId());

	private void ApplyLoaded(int peerId)
	{
		if (!_spawnOrder.Contains(peerId)) return;
		_loaded.Add(peerId);
		TryStartPlay();
	}

	// Server only. Someone who left while loading no longer holds the others back.
	private void TryStartPlay()
	{
		if (InPlay || _matchOver) return;
		if (NetworkManager.Instance.Peers.Any(id => _spawnOrder.Contains(id) && !_loaded.Contains(id))) return;
		Rpc(MethodName.StartPlay);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void StartPlay()
	{
		InPlay = true;
		EmitSignal(SignalName.PlayStarted);
	}

	// Each participant's side in the running match, fixed when it began.
	private readonly Dictionary<int, int> _matchTeams = new();

	public bool IsTeamMatch => Mode == MatchMode.TeamDeathmatch;

	// None outside a team match, and for anything off the roster.
	public int TeamOf(int participantId) =>
		IsTeamMatch && _matchTeams.TryGetValue(participantId, out int team) ? team : Teams.None;

	public bool AreTeammates(int a, int b) => a != b && TeamOf(a) != Teams.None && TeamOf(a) == TeamOf(b);

	// Kills by everyone who played on the side, including anyone who has since left.
	public int TeamKills(int team) =>
		MatchStats.Entries.Where(e => TeamOf(e.Key) == team).Sum(e => e.Value.Kills);

	public int TeamScore(int team) =>
		MatchStats.Entries.Where(e => TeamOf(e.Key) == team).Sum(e => e.Value.Score);

	// Long enough to see the explosion, short enough not to sit out the match.
	private const double RespawnDelay = 3.0;

	// What the host starts from; the lobby offers a few values around them.
	private const int DefaultKillLimit = 15;
	private const int DefaultTimeLimitMinutes = 10;

	// The server always holds peer id 1.
	private const int ServerPeerId = 1;

	// Counted down on every peer for the clock; only the server's expiry ends the match.
	private double _timeLeft;

	public double TimeRemaining => _timeLeft;

	// Set on every peer when the server calls the match, so nothing respawns afterwards.
	private bool _matchOver;

	// The winner is a participant id, or in a team match a team; None when nobody won.
	[Signal] public delegate void MatchEndedEventHandler(int winnerId);

	// A participant was destroyed. The outcome tells the caller whether to show an end-of-run
	// screen of its own; the scoreboard already covers a finished match.
	public DeathOutcome OnParticipantDestroyed(int participantId)
	{
		if (!NetworkManager.Instance.IsActive) return DeathOutcome.RunOver;
		if (_matchOver) return DeathOutcome.MatchOver;
		if (!NetworkManager.Instance.IsServer) return DeathOutcome.Respawning;

		// EventBus.Killed has already run, so the scoreboard behind this is up to date.
		int leader = Leader();
		int leaderKills = leader == Participants.None ? 0
			: IsTeamMatch ? TeamKills(leader) : MatchStats.Entries[leader].Kills;
		if (leader != Participants.None && leaderKills >= KillLimit)
		{
			Rpc(MethodName.EndMatch, leader);
			return DeathOutcome.MatchOver;
		}

		GetTree().CreateTimer(RespawnDelay).Timeout += () =>
		{
			// The match can end, or everyone can leave, while the timer runs.
			if (NetworkManager.Instance.IsActive && !_matchOver)
				Rpc(MethodName.RespawnShip, participantId, (int)(GD.Randi() >> 1));
		};
		return DeathOutcome.Respawning;
	}

	// The side ahead in a team match, None on a draw. Otherwise the highest kill count among
	// those still playing, or None on an empty board.
	private int Leader()
	{
		if (IsTeamMatch)
		{
			int one = TeamKills(Teams.One), two = TeamKills(Teams.Two);
			return one > two ? Teams.One : two > one ? Teams.Two : Teams.None;
		}

		int best = Participants.None;
		foreach (KeyValuePair<int, MatchStats.Entry> pair in MatchStats.Entries)
			if (!pair.Value.Left && (best == Participants.None || pair.Value.Kills > MatchStats.Entries[best].Kills))
				best = pair.Key;
		return best;
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void EndMatch(int winnerId)
	{
		_matchOver = true;
		EmitSignal(SignalName.MatchEnded, winnerId);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RespawnShip(int participantId, int spawnRoll)
	{
		// The level owns which ship a participant flies; this only says when, and carries the
		// server's roll for where.
		if (GetTree().CurrentScene is LevelDeathmatch level)
			level.Respawn(participantId, spawnRoll);
	}

	// Server: a player dropping out of a running match is news for everyone still in it.
	private void OnPeerLeft(int peerId)
	{
		if (!NetworkManager.Instance.IsServer) return;

		if (_ready.Remove(peerId)) BroadcastLobby();
		if (!_spawnOrder.Contains(peerId)) return;
		if (GetTree().CurrentScene is not LevelDeathmatch) return;
		Rpc(MethodName.ParticipantLeft, peerId);
		TryStartPlay();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ParticipantLeft(int participantId)
	{
		MatchStats.MarkLeft(participantId);
		if (GetTree().CurrentScene is LevelDeathmatch level)
			level.RemoveParticipant(participantId);

		// A match needs someone to play against: in a team match a side with nobody left forfeits.
		if (!NetworkManager.Instance.IsServer || _matchOver) return;

		var playing = NetworkManager.Instance.Peers.Where(id => _spawnOrder.Contains(id)).ToList();
		if (IsTeamMatch)
		{
			int[] manned = Teams.Both.Where(team => playing.Any(id => TeamOf(id) == team)).ToArray();
			if (manned.Length < 2)
				Rpc(MethodName.EndMatch, manned.Length == 1 ? manned[0] : Teams.None);
		}
		else if (playing.Count < 2)
		{
			Rpc(MethodName.EndMatch, Leader());
		}
	}

	// The session ended under a client mid-match: the host left, or removed this player.
	[Signal] public delegate void SessionLostEventHandler(string reason);

	// Client: the host is gone and the match with it. The standings stay on screen until the
	// player leaves them; the menu then shows why, as it would have straight away.
	private void OnLeftServer(string reason)
	{
		if (GetTree().CurrentScene is not LevelDeathmatch) return;
		_leaveReason = reason;
		_matchOver = true;
		// Nothing replicates the other ships any more; left alone they would drift on.
		ShipSync.Instance.Clear();
		MissileSync.Instance.Clear();
		EmitSignal(SignalName.SessionLost, reason);
	}

	// Why the last match ended when this player did not choose to leave; the menu shows it once.
	private string _leaveReason;

	public bool HasLeaveReason => _leaveReason != null;

	public string TakeLeaveReason()
	{
		string reason = _leaveReason;
		_leaveReason = null;
		return reason;
	}

	public override void _Process(double delta)
	{
		if (!InPlay)
		{
			TickLoadWait(delta);
			return;
		}

		if (_matchOver || _timeLeft <= 0.0) return;

		_timeLeft -= delta;
		if (_timeLeft > 0.0) return;

		_timeLeft = 0.0;
		if (NetworkManager.Instance.IsServer)
			Rpc(MethodName.EndMatch, Leader());
	}

	private void TickLoadWait(double delta)
	{
		if (_loadWaitLeft <= 0.0) return;

		_loadWaitLeft -= delta;
		if (_loadWaitLeft > 0.0) return;

		_loadWaitLeft = 0.0;
		if (NetworkManager.Instance.IsActive && NetworkManager.Instance.IsServer && !_matchOver)
			Rpc(MethodName.StartPlay);
	}

	// The match is over, not the session: the ships go, the lobby stays.
	public void ReturnToLobby(string menuPath = MenuScene)
	{
		_timeLeft = 0.0;
		_loadWaitLeft = 0.0;
		ShipSync.Instance.Clear();
		MissileSync.Instance.Clear();
		if (NetworkManager.Instance.IsServer)
		{
			_ready.Clear();
			BroadcastLobby();
		}
		GetTree().ChangeSceneToFile(menuPath);
	}

	// Ends the session and returns to the menu. Without this a player could walk out of a
	// match through the pause menu and leave the connection dangling behind them.
	public void LeaveMatch(string menuPath = MenuScene)
	{
		NetworkManager.Instance.Leave();
		_timeLeft = 0.0;
		_loadWaitLeft = 0.0;
		ShipSync.Instance.Clear();
		MissileSync.Instance.Clear();
		GetTree().ChangeSceneToFile(menuPath);
	}

	// -1 when the peer is not in this match.
	public int SpawnIndexOf(int peerId) => Array.IndexOf(_spawnOrder, peerId);

	// Everyone in the match, in the order the server fixed at start.
	public IReadOnlyList<int> Roster => _spawnOrder;
}
