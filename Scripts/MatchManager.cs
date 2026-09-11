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

	// Peer order fixed by the server when the match starts; a peer's position in it is its
	// spawn point. Every peer gets the same array, so spawn assignment needs no further
	// agreement.
	private int[] _spawnOrder = Array.Empty<int>();

	// Picked by the server and handed to everyone: the asteroid field derives every rock's
	// position and id from it, so without a shared seed no two machines see the same arena.
	public long WorldSeed { get; private set; } = 1;

	public override void _Ready() => Instance = this;

	public void StartMatch(string levelPath = DeathmatchLevel)
	{
		if (!NetworkManager.Instance.IsServer) return;

		long seed = (long)GD.Randi() << 32 | GD.Randi();

		// Offline the same path runs without a peer to send to.
		if (!NetworkManager.Instance.IsActive)
		{
			BeginMatch(levelPath, new[] { NetworkManager.Instance.LocalPeerId }, seed);
			return;
		}

		int[] order = NetworkManager.Instance.Peers.OrderBy(id => id).ToArray();
		Rpc(MethodName.BeginMatch, levelPath, order, seed);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void BeginMatch(string levelPath, int[] spawnOrder, long worldSeed)
	{
		_spawnOrder = spawnOrder;
		WorldSeed = worldSeed;
		_matchOver = false;
		_timeLeft = MatchDuration;
		GetTree().ChangeSceneToFile(levelPath);
	}

	// Long enough to see the explosion, short enough not to sit out the match.
	private const double RespawnDelay = 3.0;

	// First to this many kills takes the match, or the highest score when time runs out.
	// The lobby will own both of these once it exists.
	private const int KillLimit = 15;
	private const double MatchDuration = 600.0;

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
				Rpc(MethodName.RespawnShip, participantId);
		};
		return DeathOutcome.Respawning;
	}

	// Highest kill count, or None on an empty board.
	private static int Leader()
	{
		int best = Participants.None;
		foreach (KeyValuePair<int, MatchStats.Entry> pair in MatchStats.Entries)
			if (best == Participants.None || pair.Value.Kills > MatchStats.Entries[best].Kills)
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
	private void RespawnShip(int participantId)
	{
		// The level owns which ship a participant flies; this only says when.
		if (GetTree().CurrentScene is LevelDeathmatch level)
			level.Respawn(participantId);
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
