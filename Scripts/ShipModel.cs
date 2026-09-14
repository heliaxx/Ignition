using System;
using Godot;

// The visual half of a ship: engine flames and the cockpit interior. Whatever flies it says
// what is happening; the model decides how that looks, so each ship type can differ.
public partial class ShipModel : Node3D
{
	// Per second: how fast the flames follow the throttle.
	private const float FlameResponse = 8.0f;

	[Export] public MeshInstance3D[] EngineFlames = Array.Empty<MeshInstance3D>();
	[Export] public MeshInstance3D[] ReverseFlames = Array.Empty<MeshInstance3D>();

	// Only ever seen from the pilot's seat.
	[Export] public Node3D[] Interior = Array.Empty<Node3D>();

	private ShaderMaterial[] _engineMaterials;
	private ShaderMaterial[] _reverseMaterials;
	private float _forward, _reverse, _forwardTarget, _reverseTarget;

	public override void _Ready()
	{
		_engineMaterials = OwnMaterials(EngineFlames);
		_reverseMaterials = OwnMaterials(ReverseFlames);
	}

	public override void _Process(double delta)
	{
		float step = FlameResponse * (float)delta;
		_forward = Mathf.Lerp(_forward, _forwardTarget, step);
		_reverse = Mathf.Lerp(_reverse, _reverseTarget, step);

		foreach (ShaderMaterial material in _engineMaterials)
			material?.SetShaderParameter("intensity", _forward);
		foreach (ShaderMaterial material in _reverseMaterials)
			material?.SetShaderParameter("intensity", _reverse);
	}

	// Target flame intensities; the flames ease towards them.
	public void SetThrottle(float forward, float reverse)
	{
		_forwardTarget = forward;
		_reverseTarget = reverse;
	}

	public void SetInteriorVisible(bool visible)
	{
		foreach (Node3D node in Interior)
			node.Visible = visible;
	}

	// Every ThrusterFlame instance shares one material, so each flame needs its own copy
	// before its intensity is touched.
	private static ShaderMaterial[] OwnMaterials(MeshInstance3D[] flames)
	{
		var owned = new ShaderMaterial[flames.Length];
		for (int i = 0; i < flames.Length; i++)
		{
			var source = flames[i].GetSurfaceOverrideMaterial(0) as ShaderMaterial
				?? flames[i].Mesh?.SurfaceGetMaterial(0) as ShaderMaterial;
			if (source == null) continue;

			owned[i] = (ShaderMaterial)source.Duplicate();
			flames[i].SetSurfaceOverrideMaterial(0, owned[i]);
		}
		return owned;
	}
}
