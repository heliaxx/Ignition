using Godot;

// The tab row across the top of every options screen. Each tab is its own scene, which the
// OptionsMenu holding the screen swaps in.
public partial class OptionsTabs : HBoxContainer
{
	private static readonly (string Tab, string Scene)[] Tabs =
	{
		("ControlsTab", "res://Scenes/Controls.tscn"),
		("GraphicsTab", "res://Scenes/Graphics.tscn"),
		("AudioTab", "res://Scenes/Audio.tscn"),
	};

	public override void _Ready()
	{
		// Owner is the options screen this row was placed in.
		string current = Owner.SceneFilePath;
		var menu = Owner.GetParent<OptionsMenu>();
		foreach ((string name, string scene) in Tabs)
		{
			var tab = GetNode<Button>(name);
			tab.ButtonPressed = scene == current;
			tab.Pressed += () =>
			{
				if (scene != current) menu.ShowScreen(scene);
			};
		}
	}
}
