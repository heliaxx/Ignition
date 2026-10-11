using Godot;

// Places HUD indicators on the screen edge for off-screen points, and draws arrows pointing toward them.
public static class ScreenEdge
{
	// True if the point is on screen, false if it is off screen and the edge position and direction are returned.
	public static bool Locate(Camera3D camera, Vector3 point, Vector2 screenSize, float margin,
		out Vector2 position, out Vector2 direction)
	{
		Vector2 center = screenSize / 2f;
		Vector2 half = center - Vector2.One * margin;
		direction = Vector2.Zero;

		if (!camera.IsPositionBehind(point))
		{
			position = camera.UnprojectPosition(point);
			Vector2 offset = position - center;
			if (Mathf.Abs(offset.X) <= half.X && Mathf.Abs(offset.Y) <= half.Y)
				return true;
		}
		Vector3 local = camera.GlobalTransform.AffineInverse() * point;
		Vector2 toward = new Vector2(local.X, -local.Y);
		if (toward.LengthSquared() < 1e-6f) toward = Vector2.Down;
		position = center + toward * Mathf.Min(half.X / Mathf.Abs(toward.X), half.Y / Mathf.Abs(toward.Y));
		direction = toward.Normalized();
		return false;
	}

	// Draws an arrow texture that points up, turned to point along direction with its tip.
	public static void DrawArrow(CanvasItem canvas, Texture2D arrow, Vector2 tip, Vector2 direction, float size, Color color)
	{
		canvas.DrawSetTransform(tip - direction * size / 2f, direction.Angle() + Mathf.Pi / 2f);
		canvas.DrawTextureRect(arrow, new Rect2(-Vector2.One * size / 2f, Vector2.One * size), false, color);
		canvas.DrawSetTransform(Vector2.Zero);
	}
}
