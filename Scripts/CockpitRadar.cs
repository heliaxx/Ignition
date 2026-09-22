using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class CockpitRadar : Node3D
{
	[Export] public float Range = 2000f;

	// How flat the ship's plane is drawn, 1 being seen from straight above.
	private const float Flatten = 0.45f;

	private static readonly Color Grid = new(0.894118f, 0.717647f, 0.337255f, 0.6f);
	private static readonly Color Center = new(0.894118f, 0.717647f, 0.337255f, 0.80f);
	private static readonly Color Enemy = new(0.95f, 0.3f, 0.25f);
	private static readonly Color Ally = new(0.35f, 0.9f, 0.45f);
	private static readonly Color Missile = new(1f, 0.95f, 0.85f);
	private static readonly Color Locked = new(0.894118f, 0.717647f, 0.337255f, 0.85f);

	private PlayerShip _ship;
	private SubViewport _viewport;
	private Control _display;

	public override void _Ready()
	{
		_ship = GetParentOrNull<PlayerShip>();
		_viewport = GetNode<SubViewport>("Viewport");
		_display = GetNode<Control>("Viewport/Display");
		_display.Draw += DrawScanner;

		// Clear until the scanner is drawn into it, so the editor shows the bare monitor, not a white quad.
		var screen = (StandardMaterial3D)GetNode<MeshInstance3D>("Screen").MaterialOverride;
		screen.AlbedoTexture = _viewport.GetTexture();
		screen.AlbedoColor = Colors.White;

		VisibilityChanged += () => _viewport.RenderTargetUpdateMode =
			IsVisibleInTree() ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
	}

	public override void _Process(double delta)
	{
		if (IsVisibleInTree()) _display.QueueRedraw();
	}

	private void DrawScanner()
	{
		Vector2 size = _viewport.Size;
		Vector2 center = new(size.X / 2f, size.Y * 0.55f);
		float radius = size.X * 0.46f;

		DrawEllipse(center, radius, Grid);
		DrawEllipse(center, radius / 2f, Grid);
		_display.DrawLine(center + new Vector2(-radius, 0), center + new Vector2(radius, 0), Grid, 2f);
		_display.DrawLine(center + new Vector2(0, -radius * Flatten), center + new Vector2(0, radius * Flatten), Grid, 2f);
		_display.DrawColoredPolygon(new[]
		{
			center + new Vector2(0, -15), center + new Vector2(10, 7), center + new Vector2(-10, 7)
		}, Center);

		if (_ship == null) return;

		var contacts = new List<(Vector3 Local, Color Color, bool IsMissile, bool IsLocked)>();
		foreach (Node node in GetTree().GetNodesInGroup("gimbal_targets"))
		{
			if (node is not GimbalTarget gt || !gt.IsValid() || gt.TargetOwner == _ship) continue;
			contacts.Add((ToShip(gt.GlobalPosition), gt.TargetType == GimbalTargetType.Ally ? Ally : Enemy,
				false, gt == _ship.LockedTarget));
		}
		foreach (Node node in GetTree().GetNodesInGroup("missiles"))
		{
			if (node is not FlightModelMissile m || m.Exploded || m.Source == _ship) continue;
			contacts.Add((ToShip(m.GlobalPosition), Missile, true, false));
		}

		// Farther contacts sit higher on the screen and are drawn first, so nearer ones cover them.
		foreach (var c in contacts.Where(c => c.Local.Length() <= Range).OrderBy(c => c.Local.Z))
		{
			float scale = radius / Range;
			Vector2 foot = center + new Vector2(c.Local.X, c.Local.Z * Flatten) * scale;
			Vector2 tip = (foot - new Vector2(0, c.Local.Y * scale)).Clamp(Vector2.Zero, size);

			if (c.Local.Y >= 0) _display.DrawLine(foot, tip, c.Color, 4f);
			else _display.DrawDashedLine(foot, tip, c.Color, 4f, 6f);
			_display.DrawCircle(foot, 3f, c.Color);

			if (c.IsMissile)
				_display.DrawColoredPolygon(new[]
				{
					tip + new Vector2(0, -9), tip + new Vector2(7, 0), tip + new Vector2(0, 9), tip + new Vector2(-7, 0)
				}, c.Color);
			else
				_display.DrawRect(new Rect2(tip - new Vector2(7, 7), new Vector2(14, 14)), c.Color);

			if (c.IsLocked)
				_display.DrawRect(new Rect2(tip - new Vector2(13, 13), new Vector2(26, 26)), Locked, false, 3f);
		}
	}

	private Vector3 ToShip(Vector3 world) =>
		_ship.GlobalTransform.Basis.Inverse() * (world - _ship.GlobalPosition);

	private void DrawEllipse(Vector2 center, float radius, Color color)
	{
		var points = new Vector2[65];
		for (int i = 0; i < points.Length; i++)
		{
			float a = Mathf.Tau * i / (points.Length - 1);
			points[i] = center + new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius * Flatten);
		}
		_display.DrawPolyline(points, color, 3f, true);
	}
}
