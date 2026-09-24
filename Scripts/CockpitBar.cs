using Godot;

// A cockpit instrument that shows a share as a bar under its caption, or "---" when there is
// nothing to show. Runs in the editor so every instance shows its own caption while placed.
[Tool]
public partial class CockpitBar : Node3D
{
	private string _title = "";

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
}
