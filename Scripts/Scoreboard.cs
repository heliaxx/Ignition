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
	private const float FogSeconds = 0.2f;

	private const int RowFontSize = 28;
	private const int ColumnFontSize = 20;
	private const int TeamFontSize = 26;
	// Behind every row, and stronger behind the pilot's own.
	private const float RowShade = 0.18f;
	private const float OwnRowShade = 0.42f;
	// A deathmatch has one table and TDM fits two on this width.
	private const float SingleTableWidth = 1000f;

	private static readonly Color Gold = new(0.894118f, 0.717647f, 0.337255f);

	private static readonly (string Title, float Width)[] Columns =
	{
		("SCORE", 140f), ("KILLS", 110f), ("ASSISTS", 140f), ("DEATHS", 130f),
	};

	private ColorRect _fog;
	private Control _frame;
	private Control _banner;
	private Label _title;
	private CanvasItem[] _bannerGlow;
	private HBoxContainer _tables;
	private Button _leave;

	private bool _final;

	public override void _Ready()
	{
		_fog = GetNode<ColorRect>("Fog");
		_frame = GetNode<Control>("Center/Column/Frame");
		_banner = GetNode<Control>("Center/Column/Frame/Layout/Body/Content/Banner");
		_title = _banner.GetNode<Label>("Title");
		_bannerGlow = new CanvasItem[]
		{
			_banner.GetNode<CanvasItem>("Glow"), _banner.GetNode<CanvasItem>("LineTop"), _banner.GetNode<CanvasItem>("LineBottom"),
		};
		_tables = GetNode<HBoxContainer>("Center/Column/Frame/Layout/Body/Content/Tables");
		_leave = GetNode<Button>("Center/Column/LeaveButton");
		GetNode<Label>("Center/Column/Frame/Layout/Header/ModeLabel").Text =
			MatchManager.Instance.IsTeamMatch ? "TEAM DEATHMATCH" : "DEATHMATCH";

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
		MatchManager.Instance.SessionLost += OnSessionLost;
		Visible = false;
	}

	// MatchManager outlives this level, so its signal must not keep pointing here.
	public override void _ExitTree()
	{
		if (MatchManager.Instance != null)
		{
			MatchManager.Instance.MatchEnded -= OnMatchEnded;
			MatchManager.Instance.SessionLost -= OnSessionLost;
		}
	}

	public override void _Process(double _)
	{
		if (_final) return;

		bool held = Input.IsActionPressed("scoreboard");
		if (held == Visible) return;

		Visible = held;
		if (held) Rebuild();
	}

	private int LocalId => Participants.IdOf((GetTree().CurrentScene as BaseLevel)?.LocalShip);

	// Told from this pilot's side: their win is a victory, anyone else's is named.
	private void OnMatchEnded(int winnerId)
	{
		if (MatchManager.Instance.IsTeamMatch)
		{
			int mine = MatchManager.Instance.TeamOf(LocalId);
			if (winnerId == Teams.None) ShowFinal("DRAW", Gold);
			else if (winnerId == mine) ShowFinal("VICTORY", Gold);
			else ShowFinal("DEFEAT", GimbalTarget.EnemyColor);
			return;
		}

		ShowFinal(winnerId == LocalId ? "VICTORY"
			: winnerId == Participants.None ? "MATCH OVER"
			: $"{Participants.NameOf(winnerId).ToUpperInvariant()} WINS", Gold);
	}

	// The host is gone: a running match ends with the reason as its title, a finished one keeps
	// its result. Either way there is no lobby left to go back to.
	private void OnSessionLost(string reason)
	{
		if (!_final) ShowFinal(reason.ToUpperInvariant(), Gold);
		_leave.Text = "LEAVE";
	}

	private void ShowFinal(string title, Color glow)
	{
		_final = true;
		_title.Text = title;
		foreach (CanvasItem part in _bannerGlow)
			part.Modulate = glow;
		_leave.Visible = true;
		Rebuild();
		Visible = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;

		_fog.Visible = true;
		_frame.Modulate = Colors.Transparent;
		_leave.Modulate = Colors.Transparent;
		Tween tween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(_fog.Material, "shader_parameter/blur", FogBlur, FogSeconds);
		tween.TweenProperty(_fog.Material, "shader_parameter/brightness", FogBrightness, FogSeconds);
		tween.TweenProperty(_frame, "modulate", Colors.White, FogSeconds);
		tween.TweenProperty(_leave, "modulate", Colors.White, FogSeconds);
	}

	private void Rebuild()
	{
		// The banner is the result; while the match runs there is none.
		_banner.Visible = _final;
		foreach (Node table in _tables.GetChildren())
			table.QueueFree();

		List<KeyValuePair<int, MatchStats.Entry>> ranked = MatchStats.Entries
			.OrderByDescending(e => e.Value.Score)
			.ThenByDescending(e => e.Value.Kills)
			.ThenBy(e => e.Value.Deaths)
			.ToList();
		int me = LocalId;

		if (!MatchManager.Instance.IsTeamMatch)
		{
			VBoxContainer table = Table(ranked, me, Gold);
			table.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
			table.CustomMinimumSize = new Vector2(SingleTableWidth, 0);
			_tables.AddChild(table);
			return;
		}

		// Own side on the left in default color, the other on the right in the enemy color.
		int mine = MatchManager.Instance.TeamOf(me);
		foreach ((int team, string heading, Color color) in new[]
		{
			(mine, "YOUR TEAM", Gold),
			(Teams.Other(mine), "ENEMY TEAM", GimbalTarget.EnemyColor),
		})
		{
			var members = ranked.Where(e => MatchManager.Instance.TeamOf(e.Key) == team);
			_tables.AddChild(Table(members, me, color, heading, MatchManager.Instance.TeamScore(team)));
		}
	}

	private static VBoxContainer Table(IEnumerable<KeyValuePair<int, MatchStats.Entry>> entries, int me, Color color,
		string heading = null, int points = 0)
	{
		var table = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		table.AddThemeConstantOverride("separation", 4);

		if (heading != null)
		{
			var title = new HBoxContainer();
			title.AddThemeConstantOverride("separation", 12);
			title.AddChild(Text(heading, TeamFontSize, color));
			title.AddChild(Text($"{points} POINTS", TeamFontSize, Colors.White));
			table.AddChild(title);
		}

		table.AddChild(new ColorRect { Color = color, CustomMinimumSize = new Vector2(0, 2) });
		table.AddChild(Row(Columns.Select(c => c.Title).Prepend("NAME"), ColumnFontSize, color with { A = 0.65f }, null));

		foreach ((int id, MatchStats.Entry entry) in entries)
		{
			string name = entry.Name.ToUpperInvariant() + (entry.Left ? "  (LEFT)" : "");
			var cells = new[] { name, entry.Score.ToString(), entry.Kills.ToString(), entry.Assists.ToString(), entry.Deaths.ToString() };
			Color text = entry.Left ? color with { A = 0.5f } : color;
			table.AddChild(Row(cells, RowFontSize, text, color with { A = id == me ? OwnRowShade : RowShade }));
		}
		return table;
	}

	// The name takes what is left; every number sits right-aligned in its column.
	private static PanelContainer Row(IEnumerable<string> cells, int fontSize, Color text, Color? shade)
	{
		var style = new StyleBoxFlat
		{
			BgColor = shade ?? Colors.Transparent,
			ContentMarginLeft = 8,
			ContentMarginRight = 8,
			ContentMarginTop = 2,
			ContentMarginBottom = 2,
		};
		var row = new PanelContainer();
		row.AddThemeStyleboxOverride("panel", style);

		var line = new HBoxContainer();
		row.AddChild(line);
		int column = -1;
		foreach (string cell in cells)
		{
			Label label = Text(cell, fontSize, text);
			if (column < 0)
			{
				label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				label.ClipText = true;
			}
			else
			{
				label.CustomMinimumSize = new Vector2(Columns[column].Width, 0);
				label.HorizontalAlignment = HorizontalAlignment.Right;
			}
			line.AddChild(label);
			column++;
		}
		return row;
	}

	private static Label Text(string text, int fontSize, Color color)
	{
		var label = new Label { Text = text };
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", color);
		return label;
	}
}
