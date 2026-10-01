using System.Collections.Generic;
using Godot;

// Stacks menu panels over whatever is already on screen. One blur layer rides directly
// under the topmost panel, so everything behind it — earlier panels and the live 3D
// backdrop — reads as background. Nothing is paused; PauseMenu uses the same shader but
// halts the tree, this does not.
// A CanvasLayer, not a Control: its children anchor to the viewport instead of to the
// menu's fixed 1920x1080 rect, so the blur still covers the screen after a resize.
public partial class MenuStack : CanvasLayer
{
	[Export] public float FadeTime = 0.3f;
	[Export] public float BlurAmount = 2.5f;
	[Export] public float Dim = 0.6f;

	private ColorRect _blur;
	private readonly List<Control> _panels = new();

	public bool IsEmpty => _panels.Count == 0;

	// When the first panel opens and when the last one closes, so what lies under the stack
	// can step aside while a panel is up.
	[Signal] public delegate void CoveredChangedEventHandler(bool covered);

	public override void _Ready()
	{
		var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Blur.gdshader") };
		material.SetShaderParameter("blur", 0.0f);
		material.SetShaderParameter("brightness", 1.0f);

		_blur = new ColorRect
		{
			Name = "BlurLayer",
			Material = material,
			Visible = false,
			// Stop, not Ignore: a blurred menu must not still be clickable.
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		AddChild(_blur);
		_blur.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
	}

	public void Push(PackedScene panelScene)
	{
		var panel = panelScene.Instantiate<Control>();
		AddChild(panel);
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		if (IsEmpty) EmitSignal(SignalName.CoveredChanged, true);
		_panels.Add(panel);

		_blur.Visible = true;
		PositionBlur();

		// Restart the blur even when a layer is already open: what sits behind it just
		// changed, so the panel now being covered should blur in rather than snap.
		_blur.Material.Set("shader_parameter/blur", 0.0f);
		_blur.Material.Set("shader_parameter/brightness", 1.0f);
		panel.Modulate = new Color(1, 1, 1, 0);

		Tween tween = CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(_blur.Material, "shader_parameter/blur", BlurAmount, FadeTime);
		tween.TweenProperty(_blur.Material, "shader_parameter/brightness", Dim, FadeTime);
		tween.TweenProperty(panel, "modulate:a", 1.0f, FadeTime);
	}

	public void Pop()
	{
		if (IsEmpty) return;

		Control panel = _panels[^1];
		_panels.RemoveAt(_panels.Count - 1);
		bool lastOne = IsEmpty;
		if (lastOne) EmitSignal(SignalName.CoveredChanged, false);

		Tween tween = CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(panel, "modulate:a", 0.0f, FadeTime);
		if (lastOne)
		{
			tween.TweenProperty(_blur.Material, "shader_parameter/blur", 0.0f, FadeTime);
			tween.TweenProperty(_blur.Material, "shader_parameter/brightness", 1.0f, FadeTime);
		}

		tween.Finished += () =>
		{
			// RemoveChild before QueueFree: the free is deferred, and PositionBlur counts
			// children right now.
			RemoveChild(panel);
			panel.QueueFree();
			PositionBlur();
		};
	}

	// Topmost panel drawn last, blur immediately beneath it: the panel stays sharp and
	// clickable while everything below — earlier panels and the live backdrop — blurs.
	private void PositionBlur()
	{
		if (IsEmpty)
		{
			_blur.Visible = false;
			return;
		}

		MoveChild(_panels[^1], GetChildCount() - 1);
		MoveChild(_blur, GetChildCount() - 2);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (IsEmpty || !@event.IsActionPressed("menu")) return;

		Pop();
		GetViewport().SetInputAsHandled();
	}
}
