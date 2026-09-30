using Godot;

// A cockpit instrument that shows a share as a bar under its caption, or "---" when there is
// nothing to show. Runs in the editor so every instance shows its own caption while placed.
[Tool]
public partial class CockpitBar : Node3D
{
	private string _title = "";
	private HorizontalAlignment _titleAlignment = HorizontalAlignment.Center;

	[Export]
	public string Title
	{
		get => _title;
		set
		{
			_title = value;
			if (GetNodeOrNull<Label3D>("Title") is Label3D label)
				label.Text = value;
		}
	}

	[Export]
	public HorizontalAlignment TitleAlignment
	{
		get => _titleAlignment;
		set
		{
			_titleAlignment = value;
			var label = GetNodeOrNull<Label3D>("Title");
			var track = GetNodeOrNull<MeshInstance3D>("Track");
			if (label == null || track == null) return;

			float halfWidth = ((QuadMesh)track.Mesh).Size.X / 2f;
			label.HorizontalAlignment = value;
			label.Position = new Vector3(value switch
			{
				HorizontalAlignment.Left => -halfWidth,
				HorizontalAlignment.Right => halfWidth,
				_ => 0f,
			}, label.Position.Y, label.Position.Z);
		}
	}

	// A reading beside the bar, for what the bar alone cannot say; empty shows nothing.
	public string ValueText
	{
		set
		{
			if (GetNodeOrNull<Label3D>("Value") is Label3D label)
				label.Text = value;
		}
	}

	public override void _Ready()
	{
		// The scene's own root gets Title before its labels exist.
		Title = _title;
		TitleAlignment = _titleAlignment;
		SetFraction(null);
	}

	// From 0 (empty) to 1 (full), or null for no reading.
	public void SetFraction(float? fraction)
	{
		var track = GetNodeOrNull<MeshInstance3D>("Track");
		var fill = GetNodeOrNull<MeshInstance3D>("Fill");
		var empty = GetNodeOrNull<Label3D>("Empty");
		if (track == null || fill == null || empty == null) return;

		track.Visible = fraction.HasValue;
		empty.Visible = !fraction.HasValue;
		float share = Mathf.Clamp(fraction ?? 0f, 0f, 1f);
		fill.Visible = share > 0f;

		// Shrunk from the right, so the bar drains towards its left end.
		float width = ((QuadMesh)fill.Mesh).Size.X;
		fill.Scale = new Vector3(Mathf.Max(share, 0.001f), 1f, 1f);
		fill.Position = new Vector3(-(1f - share) * width / 2f, track.Position.Y, fill.Position.Z);
	}

	// A tick across the bar at a set point, from 0 (left end) to 1 (right end), or null for none.
	public void SetMarker(float? fraction)
	{
		var track = GetNodeOrNull<MeshInstance3D>("Track");
		var marker = GetNodeOrNull<MeshInstance3D>("Marker");
		if (track == null || marker == null) return;

		marker.Visible = fraction.HasValue;
		if (!fraction.HasValue) return;

		float width = ((QuadMesh)track.Mesh).Size.X;
		float share = Mathf.Clamp(fraction.Value, 0f, 1f);
		marker.Position = new Vector3((share - 0.5f) * width, track.Position.Y, marker.Position.Z);
	}
}
