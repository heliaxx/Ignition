using System.Collections.Generic;
using Godot;

// Per-match kill/death tally, fed by EventBus.Killed. Static to match EventBus: a match
// is global to the running scene, and BaseLevel._Ready resets it after EventBus.ClearAll().
public static class MatchStats
{
	public class Entry
	{
		public string Name;
		public int Kills;
		// Damaged a participant that somebody else then killed.
		public int Assists;
		public int Deaths;
		// Deaths nobody gets credit for: rammed an asteroid, own missile, level hazard.
		public int Suicides;
		// Dropped out mid-match: the row stays on the board, but cannot win.
		public bool Left;

		public int Score => Kills * KillPoints + Assists * AssistPoints;
	}

	private const int KillPoints = 100;
	private const int AssistPoints = 50;
	private const double AssistWindow = 15.0;

	// Who hit whom, and when. The time is used to determine whether a hit counts as an assist.
	private static readonly Dictionary<int, Dictionary<int, double>> _hitBy = new();

	// The time of the last hit, for assist attribution. A hit that is not followed by a kill
	// does not count as an assist, so the time of the last hit is enough
	// to know if a hit was recent enough to count.
	private static double _hitTime;

	private static readonly Dictionary<int, Entry> _entries = new();

	public static IReadOnlyDictionary<int, Entry> Entries => _entries;

	public static void Reset()
	{
		_entries.Clear();
		_hitBy.Clear();

		// EventBus.ClearAll() dropped our handler, so resubscribe; the -= guards a double Reset().
		EventBus.Killed -= OnKilled;
		EventBus.Killed += OnKilled;
	}

	// Puts a participant on the roster and the board before it has scored.
	// Safe to call twice.
	public static void Register(Node3D participant)
	{
		int id = Participants.Register(participant);
		if (id != Participants.None) Get(id);
	}

	public static void MarkLeft(int id) => Get(id).Left = true;

	public static void RecordHit(int targetId, int sourceId, double time)
	{
		_hitTime = time;
		if (targetId == Participants.None || sourceId == Participants.None || targetId == sourceId) return;

		if (!_hitBy.TryGetValue(targetId, out Dictionary<int, double> hitters))
			_hitBy[targetId] = hitters = new Dictionary<int, double>();
		hitters[sourceId] = time;
	}

	private static void OnKilled(int victim, int killer)
	{
		if (victim == Participants.None) return;

		// Everyone else who dealt damage recently shares the kill.
		if (_hitBy.Remove(victim, out Dictionary<int, double> hitters))
			foreach ((int hitter, double at) in hitters)
				if (hitter != killer && _hitTime - at <= AssistWindow) Get(hitter).Assists++;

		Get(victim).Deaths++;

		// Attribution also carries non-ships — ramming an asteroid credits the asteroid —
		// but those are off the roster, so their id is None and the death is a suicide.
		if (killer == Participants.None || killer == victim)
		{
			Get(victim).Suicides++;
			return;
		}

		Get(killer).Kills++;
	}

	private static Entry Get(int id)
	{
		if (!_entries.TryGetValue(id, out Entry entry))
		{
			entry = new Entry { Name = Participants.NameOf(id) };
			_entries[id] = entry;
		}
		return entry;
	}
}
