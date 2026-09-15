using System.Collections.Generic;
using System.Linq;
using Godot;

// Shows the match tally: held on a key during play, and permanently once the match is
// called. Reads MatchStats, which every peer builds from the same broadcast deaths, so no
// scoreboard state travels on the wire.
public partial class Scoreboard : CanvasLayer
{
	private Label _title;
	private VBoxContainer _rows;
	private Button _leave;

	private bool _final;

	public override void _Ready()
	{
		_title = GetNode<Label>("Panel/Layout/Title");
		_rows = GetNode<VBoxContainer>("Panel/Layout/Rows");
		_leave = GetNode<Button>("Panel/Layout/LeaveButton");

		MenuUtils.AttachButtonSounds(this);
		_leave.Pressed += () => MatchManager.Instance.LeaveMatch();
		_leave.Visible = false;

		MatchManager.Instance.MatchEnded += OnMatchEnded;
		Visible = false;
	}

	// MatchManager outlives this level, so its signal must not keep pointing here.
	public override void _ExitTree()
	{
		if (MatchManager.Instance != null)
			MatchManager.Instance.MatchEnded -= OnMatchEnded;
	}

	public override void _Process(double _)
	{
		if (_final) return;

		bool held = Input.IsActionPressed("scoreboard");
		if (held == Visible) return;

		Visible = held;
		if (held) Rebuild();
	}

	private void OnMatchEnded(int winnerId)
	{
		_final = true;
		_title.Text = $"{Participants.NameOf(winnerId)} WINS";
		_leave.Visible = true;
		Rebuild();
		Visible = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	private void Rebuild()
	{
		foreach (Node row in _rows.GetChildren())
			row.QueueFree();

		IEnumerable<KeyValuePair<int, MatchStats.Entry>> ranked = MatchStats.Entries
			.OrderByDescending(e => e.Value.Kills)
			.ThenBy(e => e.Value.Deaths);

		foreach (KeyValuePair<int, MatchStats.Entry> entry in ranked)
		{
			var label = new Label
			{
				Text = $"{entry.Value.Name,-16} {entry.Value.Kills,3} / {entry.Value.Deaths,-3}"
					+ (entry.Value.Suicides > 0 ? $"  ({entry.Value.Suicides} self)" : "")
					+ (entry.Value.Left ? "  (left)" : ""),
			};
			_rows.AddChild(label);
		}
	}
}
