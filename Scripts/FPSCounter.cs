using Godot;
using System;

public partial class FPSCounter : Label
{
    public override void _Ready()
    {
        LoadSettings();
        EventBus.SettingsChanged += LoadSettings;
    }

    public override void _ExitTree() => EventBus.SettingsChanged -= LoadSettings;

    private void LoadSettings()
    {
        var cfh = GetTree().Root.GetNodeOrNull<ConfigFileHandler>("/root/ConfigFileHandler");
        Visible = cfh?.GetShowFpsMeter() ?? true;
    }

    public override void _Process(double delta)
    {
        double fps = Engine.GetFramesPerSecond();
        Text = $"FPS: {fps}";
    }
}