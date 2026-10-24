using Godot;

// The options screens, one tab at a time, over whatever opened them: the main menu or a
// paused level. Tabs swap the screen in place rather than changing scene, so a level keeps
// running underneath.
public partial class OptionsMenu : CanvasLayer
{
	private const string ControlsScreen = "res://Scenes/Controls.tscn";

	// Above every layer a level draws, the pause menu and death screen included.
	private const int OptionsLayer = 20;

	private Control _screen;

	// Opens on the controls tab.
	public static OptionsMenu Open(Node parent)
	{
		var menu = new OptionsMenu { Layer = OptionsLayer, ProcessMode = ProcessModeEnum.Always };
		parent.AddChild(menu);
		menu.ShowScreen(ControlsScreen);
		return menu;
	}

	public void ShowScreen(string screenPath)
	{
		_screen?.QueueFree();
		_screen = GD.Load<PackedScene>(screenPath).Instantiate<Control>();
		AddChild(_screen);
		_screen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
	}

	public void Close()
	{
		EventBus.EmitSettingsChanged();
		QueueFree();
	}
}
