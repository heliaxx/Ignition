using Godot;

public static class Explosion
{
    private const string ScenePath = "res://Scenes/BigExplosionSpace.tscn";
    private const double Lifetime = 3.0; // animation (1.5s) + particle tails, then free

    private static PackedScene _scene;

    // The destroyed sound is opt-in: missiles blast with this too but carry their own sound.
    public static void SpawnAt(Node context, Vector3 worldPos, float scale = 1.0f, bool shipDestroyed = false)
    {
        _scene ??= GD.Load<PackedScene>(ScenePath);
        if (_scene == null || context == null) return;

        var explosion = _scene.Instantiate<Node3D>();

        Node parent = context.GetParent() ?? context.GetTree().CurrentScene;
        if (parent == null) return;

        parent.AddChild(explosion);
        explosion.GlobalPosition = worldPos;
        if (!Mathf.IsEqualApprox(scale, 1.0f))
            explosion.Scale = Vector3.One * scale;
        if (shipDestroyed)
            explosion.GetNode<AudioStreamPlayer3D>("DestroyedSound").Play();

        context.GetTree().CreateTimer(Lifetime).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(explosion))
                explosion.QueueFree();
        };
    }
}
