using Godot;

// Continuous-fire laser. Deals DPS * delta to whatever the beam hits. Scatter
// jitters the beam each frame, lowering effective DPS at range. The beam mesh
// is built procedurally in _Ready.
public partial class LaserWeapon : WeaponBase
{
    // Ship this weapon belongs to, passed to the victim for kill attribution.
    public Node3D Shooter { get; set; }

    [ExportGroup("Laser")]
    [Export] public float DPS = 15f;
    [Export] public float Range = 500f;
    [Export] public uint CollisionMask = 1;

    [ExportGroup("Scatter")]
    [Export] public float ScatterAngle = 1f;
    // Multiplies scatter by (1 + factor * hitDist/Range), growing deviation at range
    [Export] public float ScatterRangeFactor = 2f;

    [ExportGroup("Visuals")]
    [Export] public Color BeamColor = new(1f, 0.25f, 0.25f);
    [Export] public float BeamWidth = 0.04f;

    private RayCast3D _ray;
    private MeshInstance3D _beamMesh;
    private bool _firing;
    private float _lastHitDist;

    public override void _Ready()
    {
        _ray = new RayCast3D();
        _ray.TargetPosition = new Vector3(0f, 0f, -Range);
        _ray.CollisionMask = CollisionMask;
        AddChild(_ray);

        // Beam: BoxMesh with unit Z, positioned/scaled to hit distance each frame
        var box = new BoxMesh();
        box.Size = new Vector3(BeamWidth, BeamWidth, 1f);
        _beamMesh = new MeshInstance3D { Mesh = box };
        _beamMesh.Position = new Vector3(0f, 0f, -0.5f);

        var mat = new StandardMaterial3D();
        mat.EmissionEnabled = true;
        mat.Emission = BeamColor;
        mat.EmissionEnergyMultiplier = 8f;
        mat.AlbedoColor = new Color(BeamColor.R, BeamColor.G, BeamColor.B, 0.7f);
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        _beamMesh.MaterialOverride = mat;
        AddChild(_beamMesh);

        Visible = false;
    }

    public override void StartFiring()
    {
        _firing = true;
        Visible = true;
    }

    public override void StopFiring()
    {
        _firing = false;
        Visible = false;
    }

    public bool IsFiring => _firing;

    public override void _Process(double delta)
    {
        if (!Visible) return;

        if (HasGimbal) UpdateGimbal((float)delta);

        ApplyScatter();

        float beamLen = Range;
        if (_ray.IsColliding())
        {
            Vector3 hit = _ray.GetCollisionPoint();
            beamLen = GlobalPosition.DistanceTo(hit);
            _lastHitDist = beamLen;

            if (_ray.GetCollider() is IDamageable && _ray.GetCollider() is Node3D target)
            {
                CollisionShape3D shape = null;
                if (target is StaticBody3D body)
                {
                    uint owner = body.ShapeFindOwner(_ray.GetColliderShape());
                    shape = body.ShapeOwnerGetOwner(owner) as CollisionShape3D;
                }
                DamageManager.Instance.Report(target, DPS * (float)delta, shape, Shooter);
            }
        }

        _beamMesh.Scale = new Vector3(1f, 1f, beamLen);
        _beamMesh.Position = new Vector3(0f, 0f, -beamLen * 0.5f);
    }

    private void ApplyScatter()
    {
        if (ScatterAngle <= 0f)
        {
            _ray.TargetPosition = new Vector3(0f, 0f, -Range);
            return;
        }

        float effective = ScatterAngle;
        if (ScatterRangeFactor > 0f && _lastHitDist > 0f)
            effective *= 1f + ScatterRangeFactor * (_lastHitDist / Range);

        float maxRad = Mathf.DegToRad(effective);
        float phi = GD.Randf() * Mathf.Tau;
        float theta = GD.Randf() * maxRad;

        // Random direction within scatter cone around (0,0,-1) local forward
        _ray.TargetPosition = new Vector3(
             Mathf.Sin(theta) * Mathf.Cos(phi),
             Mathf.Sin(theta) * Mathf.Sin(phi),
            -Mathf.Cos(theta)
        ) * Range;
    }
}
