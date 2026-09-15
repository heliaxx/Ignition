using System.Collections.Generic;
using System.Linq;
using Godot;

// The always-on match strip: your score on the left, the clock in the middle, and whoever
// you are chasing on the right. Reads MatchStats and MatchManager; nothing here is
// networked, because both already agree on every peer.
public partial class MatchHud : CanvasLayer
{
	// Four times a second is enough for a clock that shows whole seconds, and keeps the
	// per-frame string building out of the render loop.
	private const double RefreshInterval = 0.25;

	private Label _self;
	private Label _clock;
	private Label _rival;

	private double _sinceRefresh = RefreshInterval;

	public override void _Ready()
	{
		_self = GetNode<Label>("Strip/Self");
		_clock = GetNode<Label>("Strip/Clock");
		_rival = GetNode<Label>("Strip/Rival");
	}

	public override void _Process(double delta)
	{
		_sinceRefresh += delta;
		if (_sinceRefresh < RefreshInterval) return;
		_sinceRefresh = 0.0;

		double remaining = MatchManager.Instance.TimeRemaining;
		_clock.Text = $"{(int)(remaining / 60):00}:{(int)(remaining % 60):00}";

		int localId = NetworkManager.Instance.LocalPeerId;
		_self.Text = Row(localId);

		// The board leader, or the runner-up when that is us: the point is always to show
		// who you are measured against.
		List<int> ranked = MatchStats.Entries
			.Where(e => !e.Value.Left)
			.OrderByDescending(e => e.Value.Kills)
			.ThenBy(e => e.Value.Deaths)
			.Select(e => e.Key)
			.ToList();

		int rival = ranked.FirstOrDefault(id => id != localId, Participants.None);
		_rival.Text = rival == Participants.None ? "" : Row(rival);
	}

	private static string Row(int id) =>
		MatchStats.Entries.TryGetValue(id, out MatchStats.Entry entry)
			? $"{entry.Name}  {entry.Kills}"
			: "";
}
