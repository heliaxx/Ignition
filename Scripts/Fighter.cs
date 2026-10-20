using Godot;
using System;
using System.Collections.Generic;

public partial class Fighter : CharacterBody3D, IDamageable
{
    private const float MAX_HEALTH = 200f;
    private float currentHealth = MAX_HEALTH;
    public float CurrentHealthValue => currentHealth;
    public float MaxHealthValue => MAX_HEALTH;

    [Signal]
    public delegate void DiedEventHandler();
    [Export]
    public string DisplayName = "Gryph";

    [Export]
    public float Speed = 100.0f;

    [Export]
    public float TurnSpeed = 2.0f;

    // Degrees per second the nose turns at the least. TurnSpeed alone slows to a crawl near the
    // target, so the nose would trail anything sweeping past it and never settle in the firing arc.
    [Export]
    public float MinTurnRate = 90.0f;

    [Export]
    public float MaxSteeringForce = 200.0f;

    [Export]
    public float WanderStrength = 30.0f;

    [Export]
    public float ObstacleAvoidanceForce = 400.0f;

    // Avoidance looks this far ahead at least, and further at speed: about ObstacleLookAheadTime of
    // flight, so a fast ship still sees a rock early enough to steer around it.
    [Export]
    public float ObstacleLookAhead = 100.0f;

    [Export]
    public float ObstacleLookAheadTime = 1.0f; // seconds

    [Export(PropertyHint.Layers3DPhysics)]
    public uint ObstacleCollisionMask = 1; // Layer 1 only (environment/asteroids)

    [ExportGroup("Collision")]
    [Export] public float CollisionDamageSpeedThreshold = 50.0f;
    [Export] public float CollisionDamageMultiplier = 1.0f;

    [ExportGroup("Combat")]
    [Export] public float DetectionRange = 800f;
    [Export] public float WeaponRange = 450f;
    // Closest the fighter means to get to the player: strafing runs pass this far beside them,
    // and Orbit sits this far off their side.
    [Export] public float StandoffDistance = 60f;
    // Closer than this the player has pushed in on the fighter, and it turns to circling.
    // Kept under StandoffDistance so a strafing pass is never cut short.
    [Export] public float CloseRange = 45f;
    // Radius Orbit circles the player at.
    [Export] public float OrbitDistance = 150f;
    // The laser fires in bursts: BurstDuration seconds of beam drains it, and refilling from empty
    // takes BurstRecharge seconds, a shorter burst proportionally less. Once drained, it waits
    // for a full charge rather than flickering on the dregs.
    [Export] public float BurstDuration = 1f;
    [Export] public float BurstRecharge = 3f;
    [Export] public float FiringArc = 10f; // degrees - nose cone for firing; the laser only fires straight ahead
    [Export] public float FleeHealthThreshold = 0.3f;
    [Export] public float RecentHitWindow = 1.5f; // seconds a hit still counts as recent
    // After an evade, hits cannot start another for this long, so sustained fire gets
    // bursts of evasion with fighting in between rather than one endless retreat.
    [Export] public float EvadeCooldown = 6f;

    [ExportGroup("Flight")]
    // The rate at which the throttle changes toward the target throttle, in fraction of max speed per second.
    [Export] public float ThrottleChangeRate = 0.4f;
    [Export] public float MaxBankAngle = 60f; // degrees
    // Seconds of roll per radian/second of turn: how hard the ship leans into a turn.
    [Export] public float BankPerTurnRate = 0.4f;
    [Export] public float BankResponse = 4f;

    [ExportGroup("Steering")]
    // Other fighters inside this radius push this one away, harder the closer they are.
    [Export] public float SeparationRadius = 50f;
    [Export] public float SeparationWeight = 1f;
    // Once engaged, a fighter heading further than this from the player turns back.
    [Export] public float LeashRadius = 1000f;
    [Export] public float LeashLookAhead = 1f; // seconds; the leash acts on where the ship is heading
    [Export] public float LeashSoftness = 300f; // past the radius by this much, it heads straight back

    // Cached per-frame values to avoid redundant calculations
    private Vector3 _cachedToPlayer;
    private float _cachedDistanceSquared;
    private bool _cacheValid = false;

    // Steering state
    private float _wanderAngle = 0f;

    // Physics state for obstacle avoidance
    private PhysicsDirectSpaceState3D _spaceState;
    private int _avoidanceFrameCounter = 0;
    public static int AvoidanceFrameInterval = 3; // raycast every Nth physics frame
    private Vector3 _cachedAvoidance = Vector3.Zero;
    private float _cachedAvoidanceUrgency = 0f;

    // Combat state
    private Node3D _laserNode;
    private float _recentDamageTimer = 0f;
    private bool _tookRecentDamage = false;
    private float _evadeCooldownTimer = 0f;
    private float _laserCharge; // seconds of beam left
    private bool _laserOn;
    private bool _laserDrained;
    private float _firingArcCosThreshold;

    // Breaking off at low health happens once a life; after that the fighter fights to the end.
    public bool HasFled { get; set; }
    public bool ShouldFlee => !HasFled && GetHealthPercent() <= FleeHealthThreshold;

    // Fraction of Speed the current state wants; Speed itself is the cruise.
    public float TargetThrottle { get; set; } = 1f;
    private float _throttle = 1f;
    private float MaxSpeed => Speed * _throttle;
    private float _bank;

    // Set once the fighter goes after the player; from then on it is leashed to them.
    // Before that, Idle's own wander radius keeps it in place.
    public bool Engaged { get; set; }

    // Every fighter in the tree, for separation. Pooled ones stay in the tree while parked.
    private static readonly List<Fighter> _all = new();

    // Thruster visuals
    private ShaderMaterial _thrusterMaterial;
    private ShipModel _model;
    private AudioStreamPlayer3D _engineSound;

    private Node3D player;
    private CharacterBody3D playerBody;

    public override void _EnterTree() => _all.Add(this);
    public override void _ExitTree() => _all.Remove(this);

    public override void _Ready()
    {
        MatchStats.Register(this);
        currentHealth = MAX_HEALTH;
        _laserCharge = BurstDuration;
        player = GetNode<Node3D>("../Player"); // Adjust if player path changes
        playerBody = player as CharacterBody3D;
        _laserNode = GetNodeOrNull<Node3D>("LaserWeapon");
        _firingArcCosThreshold = Mathf.Cos(Mathf.DegToRad(FiringArc));
        var thrusterMesh = GetNodeOrNull<MeshInstance3D>("ThrusterFlame");
        if (thrusterMesh != null)
            _thrusterMaterial = thrusterMesh.GetSurfaceOverrideMaterial(0) as ShaderMaterial
                ?? thrusterMesh.Mesh?.SurfaceGetMaterial(0) as ShaderMaterial;
        _model = GetNodeOrNull<ShipModel>("Model");
        _engineSound = GetNodeOrNull<AudioStreamPlayer3D>("EngineSound");
        // Nobody sits in an NPC cockpit.
        _model?.SetInteriorVisible(false);
        // Stagger avoidance frames across instances so they don't all raycast simultaneously
        _avoidanceFrameCounter = (int)(GD.Randi() % AvoidanceFrameInterval);
        SetLaserFiring(false);
        if (!HasTarget())
        {
            GD.PrintErr("Player not found in scene.");
        }
    }
    
    public void Reset()
    {
        currentHealth = MAX_HEALTH;
        _isDead = false;
        Velocity = Vector3.Zero;
        _cacheValid = false;
        _wanderAngle = GD.Randf() * Mathf.Tau;
        _tookRecentDamage = false;
        _recentDamageTimer = 0f;
        _evadeCooldownTimer = 0f;
        _laserCharge = BurstDuration;
        _laserDrained = false;
        HasFled = false;
        Engaged = false;
        _bank = 0f;
        SetLaserFiring(false);

        // Reset state machine to initial state
        var stateMachine = GetNodeOrNull<StateMachine>("StateMachine");
        stateMachine?.ResetToInitialState();
        _throttle = TargetThrottle;
    }

    public override void _PhysicsProcess(double delta)
    {
        // Invalidate cache at start of each physics frame
        _cacheValid = false;

        // Cache space state for obstacle avoidance raycasts
        _spaceState = GetWorld3D()?.DirectSpaceState;

        // Staggered obstacle avoidance - only raycast every Nth frame
        _avoidanceFrameCounter++;
        if (_avoidanceFrameCounter >= AvoidanceFrameInterval)
        {
            _avoidanceFrameCounter = 0;
            if (_spaceState != null && ObstacleAvoidanceForce > 0)
            {
                _cachedAvoidance = SteeringBehaviors.ObstacleAvoidance(
                    _spaceState,
                    GlobalPosition, Velocity, GetForward(),
                    ObstacleAvoidanceForce,
                    Mathf.Max(ObstacleLookAhead, Velocity.Length() * ObstacleLookAheadTime),
                    ObstacleCollisionMask, GetRid(),
                    out _cachedAvoidanceUrgency
                );
            }
            else
            {
                _cachedAvoidance = Vector3.Zero;
                _cachedAvoidanceUrgency = 0f;
            }
        }

        _throttle = Mathf.MoveToward(_throttle, TargetThrottle, ThrottleChangeRate * (float)delta);

        // Tick recent damage timer
        if (_recentDamageTimer > 0f)
        {
            _recentDamageTimer -= (float)delta;
            if (_recentDamageTimer <= 0f)
                _tookRecentDamage = false;
        }

        if (_evadeCooldownTimer > 0f)
            _evadeCooldownTimer -= (float)delta;

        TickLaserCharge((float)delta);

        // Update thruster intensity based on speed
        float intensity = Mathf.Clamp(Velocity.Length() / Speed * 1.2f, 0f, 1.5f);
        _thrusterMaterial?.SetShaderParameter("intensity", intensity);
        _model?.SetThrottle(intensity, 0f);
        // 1.5 is the flame at its hottest, which the engine sound takes as full power.
        if (!_isDead) PlayerShip.DriveOutsideEngine(_engineSound, intensity / 1.5f);
    }

    private void EnsureCacheValid()
    {
        if (_cacheValid || player == null) return;
        _cachedToPlayer = player.GlobalPosition - GlobalPosition;
        _cachedDistanceSquared = _cachedToPlayer.LengthSquared();
        _cacheValid = true;
    }

    public bool HasTarget() => player != null;

    public Vector3 ToPlayerVector()
    {
        EnsureCacheValid();
        return _cachedToPlayer;
    }

    public float DistanceSquaredToPlayer()
    {
        EnsureCacheValid();
        return _cachedDistanceSquared;
    }
    
    public Vector3 GetPlayerVelocity()
    {
        return playerBody?.Velocity ?? Vector3.Zero;
    }
    
    public Vector3 GetPlayerPosition()
    {
        return player?.GlobalPosition ?? GlobalPosition;
    }
    
    public Vector3 GetForward()
    {
        return -GlobalTransform.Basis.Z;
    }

    // Chase the player with lead prediction, aiming offset from them when given one.
    // skipFacing leaves rotation to the caller.
    public void PursuePlayer(float delta, bool skipFacing = false, Vector3 offset = default)
    {
        if (player == null) return;

        Vector3 steering = SteeringBehaviors.Pursue(
            GlobalPosition, Velocity, MaxSpeed,
            GetPlayerPosition() + offset, GetPlayerVelocity()
        );

        Vector3 wander = SteeringBehaviors.Wander(GetForward(), WanderStrength * 0.3f, 5f, ref _wanderAngle);
        steering += wander;

        ApplySteeringForce(steering, delta, skipFacing);
    }

    // Flee the player's predicted position.
    public void EvadePlayer(float delta)
    {
        if (player == null) return;

        Vector3 steering = SteeringBehaviors.Evade(
            GlobalPosition, Velocity, MaxSpeed,
            GetPlayerPosition(), GetPlayerVelocity()
        );

        Vector3 wander = SteeringBehaviors.Wander(GetForward(), WanderStrength * 0.5f, 8f, ref _wanderAngle);
        steering += wander;

        ApplySteeringForce(steering, delta);
    }
    
    // Drift around with forward momentum and no target.
    public void Wander(float delta)
    {
        Vector3 steering = SteeringBehaviors.Wander(GetForward(), WanderStrength, 15f, ref _wanderAngle);
        
        // Maintain some forward momentum
        steering += GetForward() * MaxSpeed * 0.5f;
        
        ApplySteeringForce(steering, delta);
    }
    
    // Adds separation, applies the leash and obstacle avoidance, moves the ship toward the
    // desired velocity, and faces the heading unless skipFacing is set. Later steps override
    // earlier ones: no state may drag a ship off the leash, and nothing may drag it into a rock.
    // Avoidance is computed on staggered physics frames.
    private void ApplySteeringForce(Vector3 steering, float delta, bool skipFacing = false)
    {
        steering += Separation() * MaxSpeed * SeparationWeight;
        steering = Leash(steering);

        // Blend in cached obstacle avoidance. The blend is stronger when avoidance
        // is urgent and when the desired steering points into the obstacle.
        if (_cachedAvoidance.LengthSquared() > 0.01f)
        {
            Vector3 avoidDir = _cachedAvoidance.Normalized();
            float conflictAmount = Mathf.Max(0, -steering.Normalized().Dot(avoidDir));
            float blendFactor = Mathf.Clamp(_cachedAvoidanceUrgency * (0.5f + conflictAmount * 0.5f), 0f, 1f);
            steering = steering.Lerp(_cachedAvoidance, blendFactor);
        }

        Velocity = SteeringBehaviors.ApplySteering(Velocity, steering, MaxSpeed, MaxSteeringForce, delta);
        Vector3 velocityBeforeSlide = Velocity;
        MoveAndSlide();
        ReportShipRams(velocityBeforeSlide);

        if (!skipFacing)
        {
            Vector3 faceDir = Velocity.LengthSquared() > 1f ? Velocity : steering;
            FaceDirection(faceDir, delta);
        }
    }

    // Reynolds' separation: away from each close fighter, from nothing at the radius to 1 when touching.
    private Vector3 Separation()
    {
        Vector3 push = Vector3.Zero;
        float radiusSq = SeparationRadius * SeparationRadius;
        foreach (Fighter other in _all)
        {
            if (other == this || other._isDead || !other.IsPhysicsProcessing()) continue;

            Vector3 away = GlobalPosition - other.GlobalPosition;
            float distSq = away.LengthSquared();
            if (distSq >= radiusSq || distSq < 0.0001f) continue;

            float dist = Mathf.Sqrt(distSq);
            push += away / dist * (1f - dist / SeparationRadius);
        }
        return push;
    }

    // Reynolds' containment, centred on the player rather than the map: the player roams
    // freely, and a fighter that strays from them is one the level cannot be finished without.
    private Vector3 Leash(Vector3 steering)
    {
        if (!Engaged || player == null) return steering;

        Vector3 heading = GlobalPosition + Velocity * LeashLookAhead;
        float beyond = heading.DistanceTo(player.GlobalPosition) - LeashRadius;
        if (beyond <= 0f) return steering;

        Vector3 back = SteeringBehaviors.Seek(GlobalPosition, player.GlobalPosition, MaxSpeed);
        return steering.Lerp(back, Mathf.Clamp(beyond / LeashSoftness, 0f, 1f));
    }

    public void MoveInDirection(Vector3 direction, float delta, bool skipFacing = false)
    {
        if (direction.LengthSquared() <= Mathf.Epsilon)
            return;

        Vector3 steering = direction.Normalized() * MaxSpeed;
        Vector3 wander = SteeringBehaviors.Wander(GetForward(), WanderStrength * 0.2f, 3f, ref _wanderAngle);
        steering += wander;

        ApplySteeringForce(steering, delta, skipFacing);
    }

    public void FaceDirection(Vector3 direction, float delta)
    {
        if (direction.LengthSquared() <= Mathf.Epsilon)
            return;

        Vector3 currentForward = -GlobalTransform.Basis.Z;
        Vector3 target = direction.Normalized();
        float angle = currentForward.AngleTo(target);
        float step = Mathf.Max(angle * TurnSpeed, Mathf.DegToRad(MinTurnRate)) * delta;

        Vector3 newForward = target;
        if (step < angle)
        {
            // Straight behind there is no shortest way round; turn about the ship's own up.
            Vector3 axis = currentForward.Cross(target);
            axis = axis.LengthSquared() > 0.000001f ? axis.Normalized() : GlobalTransform.Basis.Y.Normalized();
            newForward = currentForward.Rotated(axis, step).Normalized();
        }

        // Pointing straight up or down there is no horizon to bank against, so keep the current right.
        Vector3 right = newForward.Cross(Vector3.Up);
        right = right.LengthSquared() > 0.0001f ? right.Normalized() : GlobalTransform.Basis.X.Normalized();

        // Lean into the turn by how fast the nose is swinging sideways.
        float turnRate = (newForward - currentForward).Dot(right) / delta;
        float maxBank = Mathf.DegToRad(MaxBankAngle);
        float targetBank = Mathf.Clamp(turnRate * BankPerTurnRate, -maxBank, maxBank);
        _bank = Mathf.Lerp(_bank, targetBank, Mathf.Min(BankResponse * delta, 1f));

        Vector3 level = right.Cross(newForward);
        Vector3 up = level * Mathf.Cos(_bank) + right * Mathf.Sin(_bank);
        GlobalTransform = new Transform3D(Basis.LookingAt(newForward, up), GlobalTransform.Origin);
    }

    // Only rams into other ships: a fighter brushing a rock is left to its obstacle avoidance.
    private void ReportShipRams(Vector3 velocity)
    {
        for (int i = 0; i < GetSlideCollisionCount(); i++)
        {
            KinematicCollision3D collision = GetSlideCollision(i);
            if (collision.GetCollider() is not Node3D other || !Ramming.IsShip(other)) continue;
            Ramming.Report(this, other, Ramming.ClosingSpeed(velocity, other, collision.GetNormal()),
                CollisionDamageSpeedThreshold, CollisionDamageMultiplier);
        }
    }

    // Combat utilities

    // A request: the laser only fires while it has charge.
    public void SetLaserFiring(bool enabled)
    {
        enabled &= !_laserDrained;
        _laserOn = enabled;
        if (_laserNode == null) return;
        if (_laserNode is LaserWeapon lw)
        {
            lw.Shooter = this;
            if (enabled) lw.StartFiring();
            else lw.StopFiring();
        }
        else
        {
            _laserNode.Visible = enabled;
        }
    }

    private void TickLaserCharge(float delta)
    {
        if (_laserOn)
        {
            _laserCharge -= delta;
            if (_laserCharge > 0f) return;
            _laserCharge = 0f;
            _laserDrained = true;
            SetLaserFiring(false);
            return;
        }

        _laserCharge = Mathf.Min(BurstDuration, _laserCharge + delta * BurstDuration / BurstRecharge);
        if (_laserCharge >= BurstDuration) _laserDrained = false;
    }

    public bool IsPlayerInFiringArc()
    {
        if (player == null) return false;
        Vector3 toPlayer = (GetPlayerPosition() - GlobalPosition).Normalized();
        float dot = GetForward().Dot(toPlayer);
        return dot >= _firingArcCosThreshold;
    }

    public void FaceTarget(float delta)
    {
        if (player == null) return;
        Vector3 toPlayer = GetPlayerPosition() - GlobalPosition;
        if (toPlayer.LengthSquared() > 0.01f)
            FaceDirection(toPlayer, delta);
    }

    public float GetHealthPercent()
    {
        return currentHealth / MAX_HEALTH;
    }

    public bool TookRecentDamage => _tookRecentDamage;
    public bool ShouldEvade => _tookRecentDamage && _evadeCooldownTimer <= 0f;

    // Hits will not start an evade for the next EvadeCooldown seconds.
    public void HoldEvade() => _evadeCooldownTimer = EvadeCooldown;

    public float DistanceToPlayer()
    {
        EnsureCacheValid();
        return Mathf.Sqrt(_cachedDistanceSquared);
    }

    // Whoever dealt the most recent damage; read from a Died handler to credit a kill.
    public Node3D LastAttacker { get; private set; }

    public void TakeDamage(float amount, CollisionShape3D hitShape = null, Node3D source = null)
    {
        if (source != null && source != this)
            LastAttacker = source;

        currentHealth -= amount;
        _tookRecentDamage = true;
        _recentDamageTimer = RecentHitWindow;

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private bool _isDead;

    private void Die()
    {
        if (_isDead) return; // a second lethal hit must not spawn a second explosion
        _isDead = true;
        EventBus.EmitKilled(Participants.IdOf(this), Participants.IdOf(LastAttacker));

        Explosion.SpawnAt(this, GlobalPosition, shipDestroyed: true);
        // A pooled fighter is only hidden, so its engine would keep running.
        _engineSound?.Stop();
        EmitSignal(SignalName.Died);
    }
}