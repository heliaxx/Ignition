using Godot;

// Turns a camera while the look button is held and eases it back once released: in place for a
// cockpit view, around the ship for a chase view. Only the current camera listens, so a ship this
// machine does not fly never reacts to its mouse.
public partial class FreeLook : Camera3D
{
	// Degrees of turn per pixel of mouse travel.
	private const float Sensitivity = 0.15f;
	// Per second: the view is ~90% of the way back a quarter of a second after release.
	private const float ReturnSpeed = 10f;

	// Circle the parent's origin instead of turning in place.
	[Export] public bool OrbitParent = false;
	// Degrees either side of the rest view; 180 lets it go all the way round.
	[Export] public float MaxYaw = 140f;
	[Export] public float MinPitch = -60f;
	[Export] public float MaxPitch = 80f;

	private Transform3D _rest;
	private float _yaw;
	private float _pitch;

	// Far enough off the rest view that marks pinned to the screen centre would mislead.
	public bool IsTurned => Mathf.Abs(_yaw) > 1f || Mathf.Abs(_pitch) > 1f;

	private bool IsLooking => Current && Input.IsActionPressed("free_look");

	public override void _Ready() => _rest = Transform;

	public override void _Input(InputEvent @event)
	{
		if (@event is not InputEventMouseMotion motion || !IsLooking) return;

		// Screen pixels: Relative is rescaled by the canvas stretch, which would tie the turn rate to window size.
		_yaw -= motion.ScreenRelative.X * Sensitivity;
		_pitch = Mathf.Clamp(_pitch - motion.ScreenRelative.Y * Sensitivity, MinPitch, MaxPitch);
		_yaw = MaxYaw >= 180f ? Mathf.Wrap(_yaw, -180f, 180f) : Mathf.Clamp(_yaw, -MaxYaw, MaxYaw);
	}

	public override void _Process(double delta)
	{
		if (!IsLooking)
		{
			if (_yaw == 0f && _pitch == 0f) return;

			// Yaw stays within ±180, so easing towards zero always takes the short way back.
			float step = 1f - Mathf.Exp(-ReturnSpeed * (float)delta);
			_yaw = Settle(Mathf.Lerp(_yaw, 0f, step));
			_pitch = Settle(Mathf.Lerp(_pitch, 0f, step));
		}

		Basis turn = new Basis(Vector3.Up, Mathf.DegToRad(_yaw)) * new Basis(Vector3.Right, Mathf.DegToRad(_pitch));
		Transform = OrbitParent
			? new Transform3D(turn, Vector3.Zero) * _rest
			: new Transform3D(turn * _rest.Basis, _rest.Origin);
	}

	private static float Settle(float degrees) => Mathf.Abs(degrees) < 0.05f ? 0f : degrees;
}
