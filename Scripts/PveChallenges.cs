using System;
using Godot;

// Scenario picker for the singleplayer challenges, laid out like the multiplayer screen: the
// scenarios on the left, the chosen one explained on the right. Pushed onto the MenuStack, so
// it closes back to the main menu rather than changing scene.
public partial class PveChallenges : Control
{
	// TopScore is null for a scenario that keeps no record.
	private record Scenario(string Name, string Scene, string Description, Func<string> TopScore);

	private static readonly Scenario[] Scenarios =
	{
		new("FREE FLIGHT", "res://Scenes/LevelFreeFlight.tscn",
			"No enemies, ammo limits or other distractions. Fly around the asteroid field at your own pace, get a feel for your ship and test the controls.",
			null),
		new("RUSH", "res://Scenes/LevelRush.tscn",
			"Your hull corrodes away faster, the further you get. Fly through the rings to repair and gain points multiplier. As long as you keep moving, your score is growing.",
			() => $"TOP SCORE: {ConfigFileHandler.Instance.LoadRushHighScore()} POINTS"),
		new("WAVES", "res://Scenes/LevelWave.tscn",
			"Enemy fighters come in waves, each one bigger than the last. Hold out for as long as you can - every kill counts towards your score.",
			() => $"TOP SCORE: {ConfigFileHandler.Instance.LoadWaveHighScore()} KILLS"),
		new("SKIRMISH", "res://Scenes/LevelSkirmish.tscn",
			"A squadron of fighters abushes you as you get closer to your home base. Destroy them all before your ammo runs out. The faster you get rid of them, the better.",
			() => $"TOP SCORE: {SkirmishTime()}"),
	};

	private readonly ButtonGroup _rowGroup = new();
	private Scenario _selected = Scenarios[0];
	private Label _name;
	private Label _description;
	private Label _topScore;

	public override void _Ready()
	{
		AudioStreamPlayer click = MenuUtils.AttachButtonSounds(this);

		_name = (Label)FindChild("ScenarioName");
		_description = (Label)FindChild("Description");
		_topScore = (Label)FindChild("TopScoreLabel");

		var list = (VBoxContainer)FindChild("ScenarioList");
		foreach (Scenario scenario in Scenarios)
		{
			Button row = MenuUtils.ListRow(_rowGroup, scenario == _selected,
				MenuUtils.Cell(scenario.Name, Colors.White, expand: true));
			row.Pressed += () => ShowScenario(scenario);
			MenuUtils.WireControls(row, click);
			list.AddChild(row);
		}

		((Button)FindChild("StartButton")).Pressed += () => GetTree().ChangeSceneToFile(_selected.Scene);
		((Button)FindChild("BackButton")).Pressed += () => GetParent<MenuStack>().Pop();
		ShowScenario(_selected);
	}

	private static string SkirmishTime()
	{
		double best = ConfigFileHandler.Instance.LoadSkirmishBestTime();
		return best > 0 ? $"{LevelSkirmish.FormatTime(best)} MIN" : "—";
	}

	private void ShowScenario(Scenario scenario)
	{
		_selected = scenario;
		_name.Text = scenario.Name;
		_description.Text = scenario.Description;
		_topScore.Visible = scenario.TopScore != null;
		if (scenario.TopScore != null) _topScore.Text = scenario.TopScore();
	}
}
