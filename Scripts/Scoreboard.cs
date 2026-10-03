using System.Collections.Generic;
using System.Linq;
using Godot;

// Shows the match tally: held on a key during play, and permanently once the match is
// called. Reads MatchStats, which every peer builds from the same broadcast deaths, so no
// scoreboard state travels on the wire.
public partial class Scoreboard : CanvasLayer
{
	// The pause menu's blur and dimming, laid over the arena once the match is called.
	private const float FogBlur = 2.5f;
	private const float FogBrightness = 0.6f;
	private const float FogSeconds = 0.3f;
	// The size of the pause menu's buttons; the theme's default is made for the main menu.
	private const int RowFontSize = 30;

	private ColorRect _fog;
	private Control _panel;
	private Label _title;
	private VBoxContainer _rows;
	private Button _leave;

	private bool _final;

	public override void _Ready()
	{
		_fog = GetNode<ColorRect>("Fog");
		_panel = GetNode<Control>("Panel");
		_title = GetNode<Label>("Panel/Margin/Layout/Title");
		_rows = GetNode<VBoxContainer>("Panel/Margin/Layout/Rows");
		_leave = GetNode<Button>("Panel/Margin/Layout/LeaveButton");

		MenuUtils.AttachButtonSounds(this);
		// In a session only the match ended, so the button leads back to the lobby.
		bool networked = NetworkManager.Instance.IsActive;
		_leave.Text = networked ? "BACK TO LOBBY" : "LEAVE";
		_leave.Pressed += () =>
		{
			if (networked) MatchManager.Instance.ReturnToLobby();
			else MatchManager.Instance.LeaveMatch();
		};
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
		_title.Text = $"WINNER: {Participants.NameOf(winnerId)}";
		_leave.Visible = true;
		Rebuild();
		Visible = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;

		_fog.Visible = true;
		_panel.Modulate = Colors.Transparent;
		Tween tween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(_fog.Material, "shader_parameter/blur", FogBlur, FogSeconds);
		tween.TweenProperty(_fog.Material, "shader_parameter/brightness", FogBrightness, FogSeconds);
		tween.TweenProperty(_panel, "modulate", Colors.White, FogSeconds);
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
			label.AddThemeFontSizeOverride("font_size", RowFontSize);
			_rows.AddChild(label);
		}
	}
}
