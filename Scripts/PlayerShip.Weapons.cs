using System;
using System.Linq;
using Godot;

public partial class PlayerShip
{
    [ExportGroup("Missiles")]
    [Export] public bool UnlimitedMissiles = false;
    [Export] public int MaxMissiles = 8;
    [Export] public float MissileCooldown = 1f;
    [Export] public float MissileDamageMin = 40f;
    [Export] public float MissileDamageMax = 60f;

    private int _currentMissiles;
    public int CurrentMissiles => _currentMissiles;
    private double _timeSinceLastMissile = 0.0;
    private PackedScene _missileScene;

    [ExportGroup("Gatling")]
    [Export] public bool UnlimitedGatlingAmmo = false;
    // Rounds for all guns together, split evenly between them.
    [Export] public int GatlingAmmo = 800;

    private GatlingWeapon[] _gatlings = Array.Empty<GatlingWeapon>();

    private bool UnlimitedAmmo => _gatlings.Length > 0 && _gatlings[0].UnlimitedAmmo;
    private int _currentAmmo  => _gatlings.Sum(gun => gun.CurrentAmmo);

    // Nothing left to shoot with: every gun dry and no missiles.
    public bool OutOfAmmo => !UnlimitedAmmo && _currentAmmo <= 0 && !UnlimitedMissiles && _currentMissiles <= 0;

    private double timeSinceLastShot = 0.0;
    private double fireCooldown;

    private CockpitReadout _ammoReadout;
    private CockpitReadout _missilesReadout;

    // A shot this ship fired on the machine that owns it, replayed here for the tracer and sound.
    // Visual only — the owner reports its own hits.
    public void SpawnRelayedShot(Transform3D muzzle, Vector3 inheritedVelocity)
    {
        // Every gun relays its own shots, so the one the shot left from plays it.
        GatlingWeapon gun = _gatlings.MinBy(g => g.GlobalPosition.DistanceSquaredTo(muzzle.Origin));
        gun?.SpawnBullet(muzzle, inheritedVelocity, hasAuthority: false);
    }

    private void InitWeapons()
    {
        _missileScene = GD.Load<PackedScene>("res://Scenes/FlightModelMissile.tscn");
        _timeSinceLastMissile = MissileCooldown;
        _currentMissiles = MaxMissiles;

        _ammoReadout     = GetNodeOrNull<CockpitReadout>("AmmoReadout");
        _missilesReadout = GetNodeOrNull<CockpitReadout>("MissilesReadout");

        _gatlings = GetChildren().OfType<GatlingWeapon>().ToArray();
        foreach (GatlingWeapon gun in _gatlings)
        {
            if (UnlimitedGatlingAmmo)
                gun.UnlimitedAmmo = true;
            gun.LoadAmmo(GatlingAmmo / _gatlings.Length);
            gun.Shooter = this;
            gun.SpawnParent = GetParent() as Node3D;
            gun.AmmoChanged += (cur, max) => UpdateAmmoHUD();
        }

        fireCooldown = _gatlings.Length > 0 ? 1.0 / _gatlings[0].FireRate : 0.1;
        timeSinceLastShot = fireCooldown;

        UpdateAmmoHUD();
        UpdateMissileHUD();
    }

    private void UpdateGimbalTracking(float delta)
    {
        foreach (GatlingWeapon gun in _gatlings)
        {
            gun.SetTarget(_lockedTarget);
            gun.ShipVelocity = Velocity;
        }
    }

    private void Shoot()
    {
        if (!UnlimitedAmmo && _currentAmmo <= 0) return;

        foreach (GatlingWeapon gun in _gatlings)
        {
            Transform3D? muzzle = gun.FireOnce();
            if (muzzle.HasValue)
                WeaponSync.Instance.ReportGatlingShot(Participants.IdOf(this), muzzle.Value, Velocity);
        }
    }

    private void FireMissile()
    {
        if (_missileScene == null) return;
        if (!UnlimitedMissiles && _currentMissiles <= 0) return;
        if (_timeSinceLastMissile < MissileCooldown) return;
        if (_lockedTarget != null && IsInstanceValid(_lockedTarget) && !IsMissileLocked) return;

        // Flight-model missile (physics-based, PN guidance).
        var instance = _missileScene.Instantiate<FlightModelMissile>();
        instance.Target = (_lockedTarget != null && IsInstanceValid(_lockedTarget)) ? _lockedTarget : null;
        instance.Damage = (float)GD.RandRange(MissileDamageMin, MissileDamageMax);
        instance.InheritedVelocity = Velocity; // launch matching ship momentum
        instance.Source = this;
        instance.IgnoreBody(this); // don't detonate on the launcher

        AddCollisionExceptionWith(instance);
        instance.TreeExited += () =>
        {
            if (IsInstanceValid(this) && IsInstanceValid(instance))
                RemoveCollisionExceptionWith(instance);
        };

        // From between the guns, along their aim.
        Transform3D spawn = GlobalTransform;
        if (_gatlings.Length > 0)
        {
            spawn = _gatlings[0].GlobalTransform;
            spawn.Origin = _gatlings.Aggregate(Vector3.Zero, (sum, gun) => sum + gun.GlobalPosition) / _gatlings.Length;
        }
        spawn.Origin -= spawn.Basis.Z * 5f;
        instance.Transform = spawn;
        GetParent().AddChild(instance);
        MissileSync.Instance.ReportLaunch(instance, instance.GlobalTransform);

        if (!UnlimitedMissiles) _currentMissiles--;
        _timeSinceLastMissile = 0.0;
        UpdateMissileHUD();
    }

    private void UpdateAmmoHUD()
    {
        if (_ammoReadout == null || _gatlings.Length == 0) return;
        _ammoReadout.Value = UnlimitedAmmo ? "∞" : $"{_currentAmmo}/{_gatlings.Sum(gun => gun.MaxAmmo)}";
    }

    private void UpdateMissileHUD()
    {
        if (_missilesReadout != null)
            _missilesReadout.Value = UnlimitedMissiles ? "∞" : $"{_currentMissiles}/{MaxMissiles}";
    }

    public float GetGimbalScreenRadius() => _gatlings.Length > 0 ? _gatlings[0].GetGimbalScreenRadius() : 0f;

    // Where the guns' aim lands on screen, averaged over the guns that are in view.
    public Vector2? GetGimbalAimScreenPos()
    {
        Vector2[] points = _gatlings.Select(gun => gun.GetAimScreenPos()).Where(p => p.HasValue).Select(p => p.Value).ToArray();
        return points.Length == 0 ? null : points.Aggregate(Vector2.Zero, (sum, p) => sum + p) / points.Length;
    }

    // For the targeting code. The guns share gimbal and bullet settings, so the first stands for all.
    private Node3D barrel     => _gatlings.Length > 0 ? _gatlings[0] : null;
    private float GimbalAngle => _gatlings.Length > 0 ? _gatlings[0].GimbalAngle : 10f;
    private float BulletSpeed => _gatlings.Length > 0 ? _gatlings[0].BulletSpeed : 1600f;
}
