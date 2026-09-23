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

	[Signal] public delegate void LobbyChangedEventHandler();

	public bool IsReady(int peerId) => _ready.Contains(peerId);

	// Two ships make a match, and the host waits until the others say they are ready.
	public bool CanStart => !NetworkManager.Instance.IsActive
		|| (NetworkManager.Instance.Peers.Count >= 2
			&& NetworkManager.Instance.Peers.All(id => id == ServerPeerId || _ready.Contains(id)));

	public override void _Ready()
	{
		Instance = this;
		NetworkManager.Instance.PeerJoined += OnPeerJoined;
		NetworkManager.Instance.PeerLeft += OnPeerLeft;
		NetworkManager.Instance.LeftServer += OnLeftServer;
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

		Rpc(MethodName.SyncLobby, _ready.ToArray(), KillLimit, TimeLimitMinutes);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SyncLobby(int[] ready, int killLimit, int timeLimitMinutes)
	{
		_ready.Clear();
		foreach (int peerId in ready) _ready.Add(peerId);
		KillLimit = killLimit;
		TimeLimitMinutes = timeLimitMinutes;
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
			BeginMatch(levelPath, new[] { NetworkManager.Instance.LocalPeerId }, seed, spawnSeed, KillLimit, TimeLimitMinutes);
			return;
		}

		int[] order = NetworkManager.Instance.Peers.OrderBy(id => id).ToArray();
		Rpc(MethodName.BeginMatch, levelPath, order, seed, spawnSeed, KillLimit, TimeLimitMinutes);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void BeginMatch(string levelPath, int[] spawnOrder, long worldSeed, long spawnSeed,
		int killLimit, int timeLimitMinutes)
	{
		_spawnOrder = spawnOrder;
		WorldSeed = worldSeed;
		SpawnSeed = spawnSeed;
		KillLimit = killLimit;
		TimeLimitMinutes = timeLimitMinutes;
		_ready.Clear();
		_matchOver = false;
		_timeLeft = timeLimitMinutes * 60.0;
		GetTree().ChangeSceneToFile(levelPath);
	}

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
		if (leader != Participants.None && MatchStats.Entries[leader].Kills >= KillLimit)
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

	// Highest kill count among those still playing, or None on an empty board.
	private static int Leader()
	{
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
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ParticipantLeft(int participantId)
	{
		MatchStats.MarkLeft(participantId);
		if (GetTree().CurrentScene is LevelDeathmatch level)
			level.RemoveParticipant(participantId);

		// A match needs someone to play against.
		if (NetworkManager.Instance.IsServer && !_matchOver
			&& NetworkManager.Instance.Peers.Count(id => _spawnOrder.Contains(id)) < 2)
			Rpc(MethodName.EndMatch, Leader());
	}

	// Client: the host is gone and the match with it.
	private void OnLeftServer(string reason)
	{
		if (GetTree().CurrentScene is not LevelDeathmatch) return;
		_leaveReason = reason;
		LeaveMatch();
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
		if (_matchOver || _timeLeft <= 0.0) return;

		_timeLeft -= delta;
		if (_timeLeft > 0.0) return;

		_timeLeft = 0.0;
		if (NetworkManager.Instance.IsServer)
			Rpc(MethodName.EndMatch, Leader());
	}

	// The match is over, not the session: the ships go, the lobby stays.
	public void ReturnToLobby(string menuPath = MenuScene)
	{
		_timeLeft = 0.0;
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
		ShipSync.Instance.Clear();
		MissileSync.Instance.Clear();
		GetTree().ChangeSceneToFile(menuPath);
	}

	// -1 when the peer is not in this match.
	public int SpawnIndexOf(int peerId) => Array.IndexOf(_spawnOrder, peerId);

	// Everyone in the match, in the order the server fixed at start.
	public IReadOnlyList<int> Roster => _spawnOrder;
}
