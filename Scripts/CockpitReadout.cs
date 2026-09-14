using Godot;

// One cockpit instrument: a caption over a live value. Runs in the editor so every instance
// shows its own caption while it is being placed.
[Tool]
public partial class CockpitReadout : Node3D
{
	private string _title = "";
	private Label3D _value;

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

	public string Value
	{
		get => _value.Text;
		set => _value.Text = value;
	}

	public override void _Ready()
	{
		_value = GetNode<Label3D>("Value");
		// The scene's own root gets Title before its labels exist.
		Title = _title;
	}
}
