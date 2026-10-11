using Godot;

public partial class Ring : Area3D
{
	[Signal]
	public delegate void CollectedEventHandler(Node3D body);

	private StaticBody3D _model;
	private Node3D _detection;

	public Vector3 Center => _detection.GlobalPosition;

	public override void _Ready()
	{
		_model = GetNodeOrNull<StaticBody3D>("Model");
		_detection = GetNode<Node3D>("DetectionShape");

		BodyEntered += (body) =>
		{
			if (body is not CharacterBody3D) return;
			Deactivate();
			EmitSignal(SignalName.Collected, body);
		};
	}

	private void Deactivate()
	{
		Hide();
		SetDeferred(PropertyName.Monitoring, false);
		if (_model != null)
			_model.SetDeferred(StaticBody3D.PropertyName.CollisionLayer, 0u);
	}

	public void Activate()
	{
		if (_model != null)
			_model.CollisionLayer = 1;
		Show();
		Monitoring = true;
	}
}
