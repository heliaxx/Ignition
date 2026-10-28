using Godot;

// The options screens, one tab at a time, over whatever opened them: the main menu or a
// paused level. Tabs swap the screen in place rather than changing scene, so a level keeps
// running underneath.
public partial class OptionsMenu : CanvasLayer
{
	private const string ControlsScreen = "res://Scenes/Controls.tscn";

	// Above every layer a level draws, the pause menu and death screen included.
	private const int OptionsLayer = 20;

	// Blurs and darkens the level behind the menu.
	private const float FogBlur = 2.5f;
	private const float FogBrightness = 0.6f;
	private const float FogSeconds = 0.2f;

	private Control _screen;

	// Opens on the controls tab.
	public static OptionsMenu Open(Node parent)
	{
		var menu = new OptionsMenu { Layer = OptionsLayer, ProcessMode = ProcessModeEnum.Always };
		parent.AddChild(menu);
		menu.AddFog();
		menu.ShowScreen(ControlsScreen);
		return menu;
	}

	// Added before any screen, so every screen draws over it.
	private void AddFog()
	{
		var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Blur.gdshader") };
		material.SetShaderParameter("blur", 0.0f);
		material.SetShaderParameter("brightness", 1.0f);
		var fog = new ColorRect { Material = material };
		AddChild(fog);
		fog.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		Tween tween = CreateTween().SetParallel();
		tween.TweenProperty(material, "shader_parameter/blur", FogBlur, FogSeconds);
		tween.TweenProperty(material, "shader_parameter/brightness", FogBrightness, FogSeconds);
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
