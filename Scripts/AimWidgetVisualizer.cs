using Godot;
using System;

// Draws the HUD aiming widget: cursor, reticles, and gimbal indicator.
public partial class AimWidgetVisualizer : Control
{
	private Kaito playerShip;

	[Export] public Color RadiusColor = new Color(0f, 0.7f, 1.0f, 0.4f); // light blue, semi-transparent
	[Export] public Color DeadzoneColor = new Color(0.894f, 0.718f, 0.337f, 0.6f); // orange, more transparent
	[Export] public Color CursorColor = new Color(0.894f, 0.718f, 0.337f, 0.8f); // theme orange, opaque
	[Export] public Color CrosshairColor = new Color(0.894f, 0.718f, 0.337f, 0.9f);
	[Export] public Color TargetIndicatorColor = new Color(0.894f, 0.718f, 0.337f, 0.9f); // theme orange
	[Export] public Color LeadIndicatorColor = new Color(0.894f, 0.718f, 0.337f, 0.9f); // theme orange
	[Export] public Color GimbalColor = new Color(0.894f, 0.718f, 0.337f, 0.7f); // theme orange
	[Export] public float LineWidth = 2.0f;
	[Export] public float CursorSize = 6.0f;
	[Export] public float TargetReticleSize = 64f;
	[Export] public float LeadReticleSize = 48f;

	private Texture2D _targetReticle;
	private Texture2D _leadReticle;
	private Texture2D _gimbalReticle;
	private Texture2D _centerCrosshair;
	private Texture2D _gimbalIndicator;

	private float _flickerTimer = 0f;

	public override void _Ready()
	{
		playerShip = GetNode<Kaito>("../.."); // Navigate up to Kaito (CanvasLayer -> Kaito)
		if (playerShip == null)
		{
			GD.PrintErr("AimWidgetVisualizer: Could not find Kaito node");
		}
		SetAnchorsPreset(LayoutPreset.Center);
		SetSize(GetViewportRect().Size);

		_targetReticle = GD.Load<Texture2D>("res://Imports/Images/crosshair026.png");
		_leadReticle = GD.Load<Texture2D>("res://Imports/Images/crosshair180.png");
		_gimbalReticle = GD.Load<Texture2D>("res://Imports/Images/crosshair102.png");
		_centerCrosshair = GD.Load<Texture2D>("res://Imports/Images/crosshair001.png");
		_gimbalIndicator = GD.Load<Texture2D>("res://Imports/Images/Gimbalindicator.png");
	}

	public override void _Process(double delta)
	{
		if (playerShip == null) return;
		_flickerTimer += (float)delta;
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (playerShip == null)
			return;

		Vector2 center = GetViewportRect().Size / 2.0f;
		float radius = playerShip.AimRadius;

		// Draw outer aim radius circle
		// DrawArc(center, radius, 0f, Mathf.Tau, 64, CrosshairColor, LineWidth);

		// Draw gimbal aim reticle (converges toward lead position when tracking)
		Vector2? gimbalScreenPos = playerShip.GetGimbalAimScreenPos();
		if (_gimbalReticle != null && gimbalScreenPos.HasValue)
		{
			DrawTextureAtCenter(_gimbalReticle, gimbalScreenPos.Value, TargetReticleSize, GimbalColor);
		}

		// Pinned to the screen centre, which is the ship's heading only while the view looks straight ahead.
		if (!playerShip.IsViewTurned)
		{
			// Draw center crosshair
			if (_centerCrosshair != null)
			{
				DrawTextureAtCenter(_centerCrosshair, center, 64f, CrosshairColor);
			}

			// Draw gimbal indicator at center
			if (_gimbalIndicator != null)
			{
				DrawTextureAtCenter(_gimbalIndicator, center, 128f, CrosshairColor);
			}

			// Draw cursor indicator - fades in as it moves away from center
			Vector2 cursorPos = playerShip.GetWidgetCursorPos();
			float cursorDist = cursorPos.DistanceTo(center);
			float cursorAlpha = Mathf.Clamp(cursorDist / radius, 0f, 1f);
			Color fadedCursorColor = new Color(CursorColor.R, CursorColor.G, CursorColor.B, CursorColor.A * cursorAlpha);
			DrawCircle(cursorPos, CursorSize, fadedCursorColor);
		}

		// Draw target reticle — flickers during lock acquisition, solid when locked
		Vector2? targetScreenPos = playerShip.GetLockedTargetScreenPos();
		if (targetScreenPos.HasValue && _targetReticle != null)
		{
			float alpha = TargetIndicatorColor.A;
			if (!playerShip.IsMissileLocked)
				alpha *= 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(_flickerTimer * 12f));

			DrawTextureAtCenter(_targetReticle, targetScreenPos.Value, TargetReticleSize, TargetIndicatorColor with { A = alpha });
		}

		// Draw target lead reticle
		Vector2? leadScreenPos = playerShip.GetLeadTargetScreenPos();
		if (leadScreenPos.HasValue && _leadReticle != null)
		{
			DrawTextureAtCenter(_leadReticle, leadScreenPos.Value, LeadReticleSize, LeadIndicatorColor);
		}
	}

private void DrawTextureAtCenter(Texture2D texture, Vector2 pos, float size, Color color)
	{
		Vector2 texSize = texture.GetSize();
		float scale = size / Mathf.Max(texSize.X, texSize.Y);
		Vector2 drawSize = texSize * scale;
		Rect2 destRect = new Rect2(pos - drawSize / 2f, drawSize);
		DrawTextureRect(texture, destRect, false, color);
	}

	private void DrawArc(Vector2 center, float radius, float angleFrom, float angleTo, int segments, Color color, float width)
	{
		Vector2 prevPoint = center + new Vector2(Mathf.Cos(angleFrom), Mathf.Sin(angleFrom)) * radius;
		for (int i = 1; i <= segments; i++)
		{
			float angle = Mathf.Lerp(angleFrom, angleTo, (float)i / segments);
			Vector2 nextPoint = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
			DrawLine(prevPoint, nextPoint, color, width);
			prevPoint = nextPoint;
		}
	}
}
