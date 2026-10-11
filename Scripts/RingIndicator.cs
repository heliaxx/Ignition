using Godot;

// Marks the next Rush ring: a reticle on it while it is on screen, an arrow on the screen edge
// pointing toward it while it is not. Second ring in order marked with different reticle.
public partial class RingIndicator : Control
{
	[Export] public Color Color = new Color(0.894f, 0.718f, 0.337f, 0.9f); // theme orange
	[Export] public float EdgeMargin = 60f;
	[Export] public float MarkerSize = 64f;
	[Export] public float ArrowSize = 40f;
	[Export] public int FontSize = 22;

	public Vector3 Target;
	public Vector3? SecondTarget;

	private Texture2D _marker;
	private Texture2D _secondMarker;
	private Texture2D _arrow;

	public override void _Ready()
	{
		_marker = GD.Load<Texture2D>("res://Imports/Images/crosshair-140.png");
		_secondMarker = GD.Load<Texture2D>("res://Imports/Images/crosshair-141.png");
		_arrow = GD.Load<Texture2D>("res://Imports/Images/navigation_n.png");
	}

	public override void _Process(double delta) => QueueRedraw();

	public override void _Draw()
	{
		var camera = GetViewport().GetCamera3D();
		if (camera == null) return;

		string distance = $"{camera.GlobalPosition.DistanceTo(Target):0} m";
		Font font = GetThemeDefaultFont();
		float s = MarkerSize / 2f;

		if (SecondTarget is Vector3 second && !camera.IsPositionBehind(second))
		{
			Vector2 q = camera.UnprojectPosition(second);
			DrawTextureRect(_secondMarker, new Rect2(q - Vector2.One * s, Vector2.One * MarkerSize), false, Color);
		}

		if (ScreenEdge.Locate(camera, Target, GetViewportRect().Size, EdgeMargin, out Vector2 p, out Vector2 dir))
		{
			DrawTextureRect(_marker, new Rect2(p - Vector2.One * s, Vector2.One * MarkerSize), false, Color);
			DrawString(font, p + new Vector2(-50f, s + FontSize), distance, HorizontalAlignment.Center, 100f, FontSize, Color);
			return;
		}

		ScreenEdge.DrawArrow(this, _arrow, p, dir, ArrowSize, Color);
		Vector2 label = p - dir * (ArrowSize + FontSize * 1.5f);
		DrawString(font, label + new Vector2(-50f, FontSize * 0.35f), distance, HorizontalAlignment.Center, 100f, FontSize, Color);
	}
}
