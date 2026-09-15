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
		public int Deaths;
		// Deaths nobody gets credit for: rammed an asteroid, own missile, level hazard.
		public int Suicides;
		// Dropped out mid-match: the row stays on the board, but cannot win.
		public bool Left;
	}

	private static readonly Dictionary<int, Entry> _entries = new();

	public static IReadOnlyDictionary<int, Entry> Entries => _entries;

	public static void Reset()
	{
		_entries.Clear();

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

	private static void OnKilled(int victim, int killer)
	{
		if (victim == Participants.None) return;

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
