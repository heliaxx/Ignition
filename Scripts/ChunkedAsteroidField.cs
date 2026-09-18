using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

// Streaming asteroid field built from Poisson-disk-sampled chunks, instanced
// on the GPU via MultiMeshInstance3D.
public partial class ChunkedAsteroidField : Node3D
{
    private static readonly string[] AsteroidScenePaths =
    {
        "res://Scenes/Asteroid2.tscn",
        "res://Scenes/Asteroid3.tscn"
    };

    #region Exports

    [ExportGroup("Chunk Settings")]
    [Export]
    public float ChunkSize = 800f;

    [Export]
    public int LoadRadius = 10; // Chunks loaded around player in each direction

    [Export]
    public int UnloadBuffer = 1; // Extra distance before unloading

    [Export]
    public int CollisionRadius = 5; // Radius of chunks with collision shapes (should be <= LoadRadius)

    [ExportSubgroup("Vertical Limits")]
    [Export]
    public bool LimitUpward = false; // Limit chunk generation above Y=0

    [Export]
    public int MaxChunksUp = 2; // Max chunks above Y=0 (if LimitUpward is true)

    [Export]
    public bool LimitDownward = false; // Limit chunk generation below Y=0

    [Export]
    public int MaxChunksDown = 2; // Max chunks below Y=0 (if LimitDownward is true)

    [ExportGroup("Asteroid Settings")]
    [Export(PropertyHint.Range, "25,2000,25")]
    public float MinSpacing = 400f; // Minimum distance between asteroids for Poisson sampling

    [Export(PropertyHint.Range, "5,200,5")]
    public float MinScale = 25f; // Minimum asteroid scale

    [Export(PropertyHint.Range, "5,200,5")]
    public float MaxScale = 50f; // Maximum asteroid scale

    [Export]
    public bool AsteroidsDestroyable = true;

    [Export(PropertyHint.Range, "10,500,10")]
    public int AsteroidHP = 40;

    [ExportGroup("Spawn Clearance")]
    [Export]
    public Node3D[] SpawnExclusionZones; // Asteroids will not spawn within SpawnExclusionRadius of these nodes

    [Export]
    public float SpawnExclusionRadius = 200f; // Minimum spawn distance from exclusion zones

    [ExportGroup("Performance")]
    [Export]
    public bool UseMultiMesh = true; // GPU instancing via MultiMeshInstance3D; disable for per-asteroid MeshInstance3D

    [Export(PropertyHint.Range, "0.1,50.0,5.0")]
    public float LodBias = 15.0f; // Higher = more detail at distance

    [Export]
    public long WorldSeed = 0;

    [Export(PropertyHint.Range, "0,10,1")]
    public int LookAheadChunks = 3; // Shift load center N chunks ahead in travel direction (helps fast-moving scenarios like Rush)

    [Export(PropertyHint.Range, "1,32,1")]
    public int AsyncApplyPerFrame = 6; // Max pre-computed chunks applied to scene per frame (higher = faster fill, more CPU on apply)

    #endregion

    // Multiple asteroid variants
    private Mesh[] _asteroidMeshes;
    private Material[] _asteroidMaterials;
    private Shape3D[] _collisionShapes;

    // Loaded chunks: key = chunk coordinate hash
    private Dictionary<Vector3I, ChunkData> _loadedChunks = new();

    // Queue of chunk coords waiting to be loaded (processed gradually in _Process)
    private Queue<Vector3I> _loadQueue = new();

    // Destroyed asteroids: persisted across chunk reloads
    private HashSet<ulong> _destroyedAsteroids = new();

    // Current HP per asteroid (only tracked while above 0 and below max)
    private Dictionary<ulong, int> _asteroidHealth = new();
    // Where a rock sits in the chunk this machine built for it. Per-machine: a chunk drops
    // rocks already destroyed when it was built, so the index differs between machines.
    private sealed class AsteroidSlot
    {
        public Vector3I Chunk;
        public int MeshVariant;
        public int VariantIndex;
    }

    private readonly Dictionary<ulong, AsteroidSlot> _slots = new();

    // Player reference
    private Node3D _player;
    private Vector3I _lastPlayerChunk;

    // Field-local points kept clear of asteroids, captured once in _Ready: a clearance point
    // that moved would re-roll a chunk every time it reloaded.
    private Vector3[] _clearancePoints = Array.Empty<Vector3>();

    // Forward look-ahead: smoothed travel direction tracked from per-frame position delta
    private Vector3 _playerTravelDir = Vector3.Zero;
    private Vector3 _playerPrevPos;

    private class ChunkData
    {
        public List<MultiMeshInstance3D> MultiMeshInstances = new();
        public Dictionary<int, MultiMeshInstance3D> MmiByVariant = new();
        public Dictionary<ulong, MeshInstance3D> MeshInstanceById = new();
        public List<AsteroidInstance> Asteroids;
        public AsteroidBody CollisionBody;
    }

    private struct AsteroidInstance
    {
        public ulong Id;
        public Vector3 Position;
        public Vector3 Rotation;
        public float Scale;
        public int MeshVariant;
    }

    // Pre-computed chunk data produced on a worker thread, applied on the main thread.
    private class ChunkBuildResult
    {
        public Vector3I Coord;
        public List<AsteroidInstance> Asteroids;
        // Per mesh-variant: ordered list of pre-computed instance transforms
        public Dictionary<int, List<Transform3D>> TransformsByVariant;
    }

    // Thread-safe queue: worker threads Enqueue, main thread TryDequeue
    private readonly ConcurrentQueue<ChunkBuildResult> _builtChunks = new();
    // Chunks currently being computed on a worker thread (main-thread only access)
    private readonly HashSet<Vector3I> _chunksBeingBuilt = new();
    // Built chunks waiting to be applied, re-prioritized by distance each frame
    private readonly List<ChunkBuildResult> _applyBuffer = new();

    public override void _Ready()
    {
        // Generate random seed if not set
        if (WorldSeed == 0)
        {
            GD.Randomize();
            WorldSeed = (long)((ulong)GD.Randi() << 32 | GD.Randi());
        }
        
        AddToGroup("asteroid_field");
        LoadAsteroidMeshes();

        // Find player - adjust path as needed
        _player = GetTree().GetFirstNodeInGroup("Player") as Node3D;
        if (_player == null)
        {
            GD.PrintErr("ChunkedAsteroidField: No player found in 'Player' group");
            return;
        }

        _lastPlayerChunk = WorldToChunk(_player.GlobalPosition);
        _playerPrevPos = _player.GlobalPosition;
        SnapshotClearancePoints();
        UpdateChunks();

        // Pre-load all initial chunks synchronously so the level starts fully populated
        while (_loadQueue.Count > 0)
        {
            var coord = _loadQueue.Dequeue();
            if (!_loadedChunks.ContainsKey(coord))
                LoadChunk(coord);
        }
    }

    public override void _Process(double delta)
    {
        if (_player == null) return;

        // Track smoothed travel direction from position delta (no Godot velocity access needed).
        // Cap at ChunkSize to ignore large jumps caused by floating-point origin rebasing
        // (BaseLevel._Process runs before children, so an origin shift produces a huge
        // apparent delta of ~OriginShiftThreshold in this same frame).
        Vector3 posDelta = _player.GlobalPosition - _playerPrevPos;
        float deltaSq = posDelta.LengthSquared();
        if (deltaSq > 1f && deltaSq < ChunkSize * ChunkSize)
            _playerTravelDir = posDelta.Normalized();
        _playerPrevPos = _player.GlobalPosition;

        Vector3I currentChunk = WorldToChunk(_player.GlobalPosition);

        // Only update when player crosses chunk boundary
        if (currentChunk != _lastPlayerChunk)
        {
            _lastPlayerChunk = currentChunk;
            UpdateChunks();
        }

        // Gradually load queued chunks to avoid frame spikes
        ProcessChunkQueue();
    }

    private void LoadAsteroidMeshes()
    {
        var meshes = new List<Mesh>();
        var materials = new List<Material>();
        var collisionShapes = new List<Shape3D>();

        foreach (var scenePath in AsteroidScenePaths)
        {
            var asteroidScene = GD.Load<PackedScene>(scenePath);
            if (asteroidScene == null)
            {
                GD.PrintErr($"ChunkedAsteroidField: Could not load asteroid scene '{scenePath}'");
                continue;
            }

            var instance = asteroidScene.Instantiate<Node3D>();
            var meshInstance = FindMeshInstance(instance);

            if (meshInstance?.Mesh != null)
            {
                meshes.Add(meshInstance.Mesh);

                Material material = meshInstance.GetActiveMaterial(0);
                if (material == null)
                {
                    material = meshInstance.Mesh.SurfaceGetMaterial(0);
                }
                materials.Add(material);

                var collisionShape = FindCollisionShape(instance);
                if (collisionShape?.Shape != null)
                {
                    collisionShapes.Add(collisionShape.Shape);
                }
                else
                {
                    collisionShapes.Add(null);
                    GD.PrintErr($"ChunkedAsteroidField: No collision shape found in '{scenePath}'");
                }
            }
            else
            {
                GD.PrintErr($"ChunkedAsteroidField: Could not extract mesh from asteroid scene '{scenePath}'");
            }

            instance.QueueFree();
        }

        _asteroidMeshes = meshes.ToArray();
        _asteroidMaterials = materials.ToArray();
        _collisionShapes = collisionShapes.ToArray();

        if (_asteroidMeshes.Length == 0)
        {
            GD.PrintErr("ChunkedAsteroidField: No asteroid meshes loaded");
        }

        if (_collisionShapes.Length == 0)
        {
            GD.PrintErr("ChunkedAsteroidField: No collision shapes loaded!");
        }
    }

    private MeshInstance3D FindMeshInstance(Node node)
    {
        if (node is MeshInstance3D mi && mi.Mesh != null)
            return mi;

        foreach (var child in node.GetChildren())
        {
            var result = FindMeshInstance(child);
            if (result != null) return result;
        }
        return null;
    }

    private CollisionShape3D FindCollisionShape(Node node)
    {
        if (node is CollisionShape3D cs && cs.Shape != null)
            return cs;

        foreach (var child in node.GetChildren())
        {
            var result = FindCollisionShape(child);
            if (result != null) return result;
        }
        return null;
    }

    private void UpdateChunks()
    {
        Vector3I playerChunk = _lastPlayerChunk;
        Vector3I loadCenter = GetLoadCenter();

        // Build the set of chunks that should be loaded: all coords within a spherical
        // radius of LoadRadius chunks around loadCenter, respecting optional Y clamping.
        HashSet<Vector3I> shouldBeLoaded = new();

        for (int x = -LoadRadius; x <= LoadRadius; x++)
        {
            for (int y = -LoadRadius; y <= LoadRadius; y++)
            {
                for (int z = -LoadRadius; z <= LoadRadius; z++)
                {
                    // Sphere check: skip corners of the cube that exceed LoadRadius
                    if (x * x + y * y + z * z <= LoadRadius * LoadRadius + 1)
                    {
                        int chunkY = loadCenter.Y + y;

                        // Vertical limits check: skip chunks exceeding upward/downward limits, if enabled
                        if (LimitUpward && chunkY > MaxChunksUp)
                            continue;
                        if (LimitDownward && chunkY < -MaxChunksDown)
                            continue;

                        shouldBeLoaded.Add(loadCenter + new Vector3I(x, y, z));
                    }
                }
            }
        }

        // Unload chunks that have gone beyond LoadRadius + UnloadBuffer from loadCenter.
        // Using loadCenter (not playerChunk) keeps unload consistent with the shifted load sphere.
        List<Vector3I> toUnload = new();
        int unloadDist = LoadRadius + UnloadBuffer;

        foreach (var kvp in _loadedChunks)
        {
            Vector3I diff = kvp.Key - loadCenter;
            if (Mathf.Abs(diff.X) > unloadDist ||
                Mathf.Abs(diff.Y) > unloadDist ||
                Mathf.Abs(diff.Z) > unloadDist)
            {
                toUnload.Add(kvp.Key);
            }
        }

        foreach (var coord in toUnload)
        {
            UnloadChunk(coord);
        }

        // Enqueue not-yet-loaded chunks, nearest first, so the area immediately
        // around the player always populates before distant chunks.
        // Skip chunks already being computed on a worker thread (_chunksBeingBuilt).
        _loadQueue.Clear();
        var pending = new List<Vector3I>();
        foreach (var coord in shouldBeLoaded)
        {
            if (!_loadedChunks.ContainsKey(coord) && !_chunksBeingBuilt.Contains(coord))
                pending.Add(coord);
        }
        pending.Sort((a, b) =>
        {
            var da = a - playerChunk;
            var db = b - playerChunk;
            return (da.X * da.X + da.Y * da.Y + da.Z * da.Z)
                .CompareTo(db.X * db.X + db.Y * db.Y + db.Z * db.Z);
        });
        foreach (var coord in pending)
            _loadQueue.Enqueue(coord);

        // Add/remove collision bodies on chunks based on CollisionRadius
        UpdateCollisionBodies();
    }

    // Synchronous entry point used by _Ready for initial population.
    private void LoadChunk(Vector3I coord)
    {
        ApplyChunkData(ComputeChunkData(coord, new HashSet<ulong>(_destroyedAsteroids)));
    }

    // Pure computation — safe on a worker thread: the Godot types used here are value-type
    // structs, and _asteroidMeshes and _clearancePoints are set once in _Ready.
    private ChunkBuildResult ComputeChunkData(Vector3I coord, HashSet<ulong> destroyedSnapshot)
    {
        ulong chunkSeed = GenerateChunkSeed(coord);
        Vector3 regionSize = new Vector3(ChunkSize, ChunkSize, ChunkSize);
        var points = PoissonDiskSampler.GeneratePoints(regionSize, MinSpacing, chunkSeed);

        // Use System.Random (thread-safe) seeded deterministically from the chunk seed
        var rng = new Random((int)(chunkSeed ^ (chunkSeed >> 32)));

        List<AsteroidInstance> asteroids = new();
        Vector3 chunkOrigin = ChunkToWorld(coord);
        float radiusSq = SpawnExclusionRadius * SpawnExclusionRadius;
        int meshCount = _asteroidMeshes.Length;

        foreach (var localPos in points)
        {
            // Drawn for every point, kept or not, so skipping one cannot shift the rest.
            var rotation = new Vector3(
                (float)(rng.NextDouble() * Mathf.Tau),
                (float)(rng.NextDouble() * Mathf.Tau),
                (float)(rng.NextDouble() * Mathf.Tau)
            );
            float scale = MinScale + (float)(rng.NextDouble() * (MaxScale - MinScale));
            int meshVariant = meshCount > 0 ? rng.Next(0, meshCount) : 0;

            ulong asteroidId = GenerateAsteroidId(coord, localPos);
            if (destroyedSnapshot.Contains(asteroidId)) continue;

            Vector3 pos = chunkOrigin + localPos - regionSize * 0.5f; // field-local

            bool cleared = false;
            foreach (var cp in _clearancePoints)
            {
                if (pos.DistanceSquaredTo(cp) < radiusSq) { cleared = true; break; }
            }
            if (cleared) continue;

            asteroids.Add(new AsteroidInstance
            {
                Id          = asteroidId,
                Position    = pos,
                Rotation    = rotation,
                Scale       = scale,
                MeshVariant = meshVariant
            });
        }

        // Pre-compute Transform3D for each asteroid so ApplyChunkData only creates Godot nodes
        var transformsByVariant = new Dictionary<int, List<Transform3D>>();
        foreach (var a in asteroids)
        {
            if (!transformsByVariant.TryGetValue(a.MeshVariant, out var list))
                transformsByVariant[a.MeshVariant] = list = new List<Transform3D>();

            var localPos2 = a.Position - chunkOrigin;
            list.Add(new Transform3D(MakeBasis(a.Rotation, a.Scale), localPos2));
        }

        return new ChunkBuildResult
        {
            Coord              = coord,
            Asteroids          = asteroids,
            TransformsByVariant = transformsByVariant
        };
    }

    // Must run on the main thread — creates Godot nodes and calls AddChild.
    private void ApplyChunkData(ChunkBuildResult result)
    {
        var coord       = result.Coord;
        var asteroids   = result.Asteroids;
        Vector3 chunkOrigin = ChunkToWorld(coord);

        List<MultiMeshInstance3D> mmis = new();
        var mmiByVariant    = new Dictionary<int, MultiMeshInstance3D>();
        var meshInstanceById = new Dictionary<ulong, MeshInstance3D>();

        if (_asteroidMeshes.Length > 0 && asteroids.Count > 0)
        {
            if (UseMultiMesh)
            {
                for (int meshIndex = 0; meshIndex < _asteroidMeshes.Length; meshIndex++)
                {
                    if (_asteroidMeshes[meshIndex] == null) continue;
                    if (!result.TransformsByVariant.TryGetValue(meshIndex, out var transforms)) continue;
                    if (transforms.Count == 0) continue;

                    var multiMesh = new MultiMesh();
                    multiMesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
                    multiMesh.Mesh            = _asteroidMeshes[meshIndex];
                    multiMesh.InstanceCount   = transforms.Count;

                    for (int i = 0; i < transforms.Count; i++)
                        multiMesh.SetInstanceTransform(i, transforms[i]);

                    var mmi = new MultiMeshInstance3D();
                    mmi.Multimesh  = multiMesh;
                    mmi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
                    mmi.LodBias    = LodBias;
                    mmi.Position   = chunkOrigin;

                    if (_asteroidMaterials != null && meshIndex < _asteroidMaterials.Length)
                        mmi.MaterialOverride = _asteroidMaterials[meshIndex];

                    AddChild(mmi);
                    mmis.Add(mmi);
                    mmiByVariant[meshIndex] = mmi;
                }
            }
            else
            {
                foreach (var a in asteroids)
                {
                    if (a.MeshVariant < 0 || a.MeshVariant >= _asteroidMeshes.Length) continue;
                    var mesh = _asteroidMeshes[a.MeshVariant];
                    if (mesh == null) continue;

                    var mi = new MeshInstance3D();
                    mi.Mesh       = mesh;
                    mi.Transform  = new Transform3D(MakeBasis(a.Rotation, a.Scale), a.Position);
                    mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
                    mi.LodBias    = LodBias;

                    if (_asteroidMaterials != null && a.MeshVariant < _asteroidMaterials.Length)
                        mi.MaterialOverride = _asteroidMaterials[a.MeshVariant];

                    AddChild(mi);
                    meshInstanceById[a.Id] = mi;
                }
            }
        }

        var chunkData = new ChunkData
        {
            MultiMeshInstances = mmis,
            MmiByVariant       = mmiByVariant,
            MeshInstanceById   = meshInstanceById,
            Asteroids          = asteroids,
            CollisionBody      = null
        };

        var variantCounts = new int[Mathf.Max(_asteroidMeshes.Length, 1)];
        foreach (var a in asteroids)
        {
            int variantIndex = (a.MeshVariant >= 0 && a.MeshVariant < variantCounts.Length)
                ? variantCounts[a.MeshVariant]++
                : 0;
            _slots[a.Id] = new AsteroidSlot
            {
                Chunk = coord,
                MeshVariant = a.MeshVariant,
                VariantIndex = variantIndex,
            };
        }

        _loadedChunks[coord] = chunkData;

        // Destroyed since the build was dispatched, so the worker's snapshot missed it and it
        // is still in the mesh. CreateCollisionBody below skips it either way.
        foreach (var a in asteroids)
            if (_destroyedAsteroids.Contains(a.Id))
                HideAsteroid(a.Id);

        if (IsWithinCollisionRadius(coord))
            CreateCollisionBody(chunkData);
    }

    // Field-local, so an origin shift moves the field and the points together.
    private void SnapshotClearancePoints()
    {
        var points = new List<Vector3> { ToLocal(_player.GlobalPosition) };

        if (SpawnExclusionZones != null)
            foreach (var zone in SpawnExclusionZones)
                if (zone != null) points.Add(ToLocal(zone.GlobalPosition));

        _clearancePoints = points.ToArray();
    }

    // Load center, shifted ahead in the direction of travel (helps fast scenarios
    // like Rush). When LookAheadChunks > 0 the sphere is biased forward so the player
    // has more pre-loaded space in front and less behind.
    private Vector3I GetLoadCenter()
    {
        Vector3I lookAheadOffset = Vector3I.Zero;
        if (LookAheadChunks > 0 && _playerTravelDir.LengthSquared() > 0.1f)
        {
            lookAheadOffset = new Vector3I(
                Mathf.RoundToInt(_playerTravelDir.X * LookAheadChunks),
                Mathf.RoundToInt(_playerTravelDir.Y * LookAheadChunks),
                Mathf.RoundToInt(_playerTravelDir.Z * LookAheadChunks)
            );
        }
        return _lastPlayerChunk + lookAheadOffset;
    }

    // True if coord is still inside the unload-buffer zone around the current load center.
    private bool IsWithinLoadRange(Vector3I coord)
    {
        Vector3I diff = coord - GetLoadCenter();
        int d = LoadRadius + UnloadBuffer;
        return Mathf.Abs(diff.X) <= d && Mathf.Abs(diff.Y) <= d && Mathf.Abs(diff.Z) <= d;
    }

    private void UnloadChunk(Vector3I coord)
    {
        if (_loadedChunks.TryGetValue(coord, out var chunk))
        {
            foreach (var mmi in chunk.MultiMeshInstances)
                mmi?.QueueFree();

            foreach (var mi in chunk.MeshInstanceById.Values)
                mi?.QueueFree();

            chunk.CollisionBody?.QueueFree();
            if (chunk.Asteroids != null)
                foreach (var a in chunk.Asteroids)
                    _slots.Remove(a.Id);

            _loadedChunks.Remove(coord);
        }
    }

    private Vector3I WorldToChunk(Vector3 worldPos)
    {
        // Convert to field-local space so chunk selection is correct
        // regardless of the field node's world position (supports origin rebasing).
        Vector3 local = worldPos - GlobalPosition;
        return new Vector3I(
            Mathf.FloorToInt(local.X / ChunkSize),
            Mathf.FloorToInt(local.Y / ChunkSize),
            Mathf.FloorToInt(local.Z / ChunkSize)
        );
    }

    private Vector3 ChunkToWorld(Vector3I chunkCoord) => (Vector3)chunkCoord * ChunkSize;

    // Builds an asteroid basis from per-axis rotation (radians) and uniform scale.
    private static Basis MakeBasis(Vector3 rotation, float scale)
    {
        var basis = Basis.Identity;
        basis = basis.Rotated(Vector3.Right,   rotation.X);
        basis = basis.Rotated(Vector3.Up,      rotation.Y);
        basis = basis.Rotated(Vector3.Forward, rotation.Z);
        return scale == 1f ? basis : basis.Scaled(new Vector3(scale, scale, scale));
    }

    // 21 bits per axis: ±1M chunks, far past anything reachable.
    private const ulong AxisMask = 0x1FFFFF;

    private ulong GenerateChunkSeed(Vector3I coord)
    {
        // Axes occupy disjoint bit ranges: two chunks sharing a seed would hold identical
        // rocks under identical ids.
        unchecked
        {
            ulong hash = (ulong)WorldSeed;
            hash ^= ((ulong)coord.X & AxisMask)
                  | (((ulong)coord.Y & AxisMask) << 21)
                  | (((ulong)coord.Z & AxisMask) << 42);

            // MurmurHash3 finalizer: avalanches the packed axes across all 64 bits.
            hash ^= hash >> 33;
            hash *= 0xff51afd7ed558ccdUL;
            hash ^= hash >> 33;
            hash *= 0xc4ceb9fe1a85ec53UL;
            hash ^= hash >> 33;
            return hash;
        }
    }

    private ulong GenerateAsteroidId(Vector3I chunk, Vector3 localPos)
    {
        // Create unique ID for persistence tracking
        unchecked
        {
            ulong id = GenerateChunkSeed(chunk);
            id ^= (ulong)(localPos.X * 12345.67f).GetHashCode();
            id ^= (ulong)(localPos.Y * 98765.43f).GetHashCode() << 16;
            id ^= (ulong)(localPos.Z * 54321.09f).GetHashCode() << 32;
            return id;
        }
    }

    // Records an asteroid as destroyed so it stays gone across chunk reloads.
    public void MarkAsteroidDestroyed(ulong asteroidId)
    {
        _destroyedAsteroids.Add(asteroidId);
    }

    // Reports whether the damage killed the rock; the caller owns the destruction, so only
    // one machine decides it. Networked, that is the server.
    public bool DamageAsteroid(ulong asteroidId, int damage)
    {
        if (!AsteroidsDestroyable) return false;
        if (_destroyedAsteroids.Contains(asteroidId)) return false;

        if (!_asteroidHealth.TryGetValue(asteroidId, out int hp))
            hp = AsteroidHP;

        hp -= damage;

        if (hp > 0)
        {
            _asteroidHealth[asteroidId] = hp;
            return false;
        }

        _asteroidHealth.Remove(asteroidId);
        return true;
    }

    // Collision is rebuilt rather than switched off shape by shape: CreateCollisionBody skips
    // everything in _destroyedAsteroids, so the body cannot drift out of step with the record.
    // Safe to call for a rock whose chunk this machine has not built.
    public void DestroyAsteroid(ulong asteroidId)
    {
        MarkAsteroidDestroyed(asteroidId);

        if (_slots.TryGetValue(asteroidId, out AsteroidSlot slot)
            && _loadedChunks.TryGetValue(slot.Chunk, out ChunkData chunk)
            && chunk.CollisionBody != null)
        {
            chunk.CollisionBody.QueueFree();
            chunk.CollisionBody = null;
            CreateCollisionBody(chunk);
        }

        HideAsteroid(asteroidId);
    }

    // Collapses the rock's instance in whichever chunk this machine built it into.
    private void HideAsteroid(ulong asteroidId)
    {
        if (!_slots.TryGetValue(asteroidId, out AsteroidSlot slot)) return;
        if (!_loadedChunks.TryGetValue(slot.Chunk, out ChunkData chunk)) return;

        if (UseMultiMesh)
        {
            if (chunk.MmiByVariant.TryGetValue(slot.MeshVariant, out var mmi))
            {
                var t = mmi.Multimesh.GetInstanceTransform(slot.VariantIndex);
                t.Basis = Basis.Identity.Scaled(Vector3.Zero);
                mmi.Multimesh.SetInstanceTransform(slot.VariantIndex, t);
            }
        }
        else if (chunk.MeshInstanceById.TryGetValue(asteroidId, out var mi))
        {
            mi.Visible = false;
        }
    }


    private bool IsWithinCollisionRadius(Vector3I coord)
    {
        Vector3I diff = coord - _lastPlayerChunk;
        return diff.X * diff.X + diff.Y * diff.Y + diff.Z * diff.Z
               <= CollisionRadius * CollisionRadius;
    }

    // Builds a StaticBody3D with one shape per live asteroid in the chunk.
    // No-op if the chunk already has a collision body.
    private void CreateCollisionBody(ChunkData chunk)
    {
        if (_collisionShapes == null || _collisionShapes.Length == 0) return;
        if (chunk.CollisionBody != null) return;

        var collisionBody = new AsteroidBody { Field = this };
        collisionBody.AddToGroup("asteroids");

        foreach (var a in chunk.Asteroids)
        {
            // No collision shape needed for already-destroyed asteroids
            if (_destroyedAsteroids.Contains(a.Id))
                continue;

            Shape3D shape = null;
            if (a.MeshVariant >= 0 && a.MeshVariant < _collisionShapes.Length)
                shape = _collisionShapes[a.MeshVariant];
            if (shape == null && _collisionShapes.Length > 0)
                shape = _collisionShapes[0];
            if (shape == null)
                continue;

            var collisionShape = new CollisionShape3D();
            collisionShape.Shape = shape;
            collisionShape.Position = a.Position;
            collisionShape.Basis = MakeBasis(a.Rotation, 1f);
            collisionShape.Scale = new Vector3(a.Scale, a.Scale, a.Scale);
            collisionShape.SetMeta("asteroid_id", (long)a.Id);
            collisionBody.AddChild(collisionShape);
        }

        // Add after all shapes - single physics-server notification
        AddChild(collisionBody);
        chunk.CollisionBody = collisionBody;
    }

    public void ApplyLodBias(float bias)
    {
        foreach (var chunk in _loadedChunks.Values)
        {
            foreach (var mmi in chunk.MultiMeshInstances)
                if (mmi != null) mmi.LodBias = bias;
            foreach (var mi in chunk.MeshInstanceById.Values)
                if (mi != null) mi.LodBias = bias;
        }
    }

    private void UpdateCollisionBodies()
    {
        foreach (var kvp in _loadedChunks)
        {
            var chunk = kvp.Value;
            bool within = IsWithinCollisionRadius(kvp.Key);

            if (within && chunk.CollisionBody == null)
                CreateCollisionBody(chunk);
            else if (!within && chunk.CollisionBody != null)
            {
                chunk.CollisionBody.QueueFree();
                chunk.CollisionBody = null;
            }
        }
    }

    // Gradual chunk loading (async)

    // Per frame: apply up to AsyncApplyPerFrame finished chunks from the worker
    // queue, then dispatch the remaining queued coords to background tasks.
    private void ProcessChunkQueue()
    {
        // Collect finished builds into the apply buffer. Coords stay in
        // _chunksBeingBuilt until applied or discarded so they aren't re-dispatched.
        while (_builtChunks.TryDequeue(out var built))
            _applyBuffer.Add(built);

        // Drop chunks that got loaded by another path or fell out of range.
        _applyBuffer.RemoveAll(r =>
        {
            if (_loadedChunks.ContainsKey(r.Coord) || !IsWithinLoadRange(r.Coord))
            {
                _chunksBeingBuilt.Remove(r.Coord);
                return true;
            }
            return false;
        });

        // Apply the chunks nearest the (forward-shifted) load center first, so the
        // corridor ahead of the player populates before the wake behind them.
        // Applying is just node creation; the heavy sampling already ran off-thread.
        Vector3I center = GetLoadCenter();
        _applyBuffer.Sort((a, b) =>
        {
            Vector3I da = a.Coord - center;
            Vector3I db = b.Coord - center;
            return (da.X * da.X + da.Y * da.Y + da.Z * da.Z)
                .CompareTo(db.X * db.X + db.Y * db.Y + db.Z * db.Z);
        });

        int applyCount = Mathf.Min(AsyncApplyPerFrame, _applyBuffer.Count);
        for (int i = 0; i < applyCount; i++)
        {
            var result = _applyBuffer[i];
            _chunksBeingBuilt.Remove(result.Coord);
            ApplyChunkData(result);
        }
        _applyBuffer.RemoveRange(0, applyCount);

        // Dispatch pending coords to worker threads.
        while (_loadQueue.Count > 0)
        {
            var coord = _loadQueue.Dequeue();
            if (_loadedChunks.ContainsKey(coord) || _chunksBeingBuilt.Contains(coord))
                continue;

            _chunksBeingBuilt.Add(coord);

            // Snapshot main-thread-only data before entering the lambda
            var destroyedSnap = new HashSet<ulong>(_destroyedAsteroids);

            Task.Run(() =>
            {
                var built = ComputeChunkData(coord, destroyedSnap);
                _builtChunks.Enqueue(built);
            });
        }
    }
}
