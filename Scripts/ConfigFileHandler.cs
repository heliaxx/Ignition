using Godot;
using System.Collections.Generic;

public partial class ConfigFileHandler : Node
{
	private ConfigFile config = new ConfigFile();
	private const string SETTINGS_FILE_PATH = "user://settings.ini";

	public static ConfigFileHandler Instance;

	public override void _Ready()
	{
		if (!FileAccess.FileExists(SETTINGS_FILE_PATH))
		{
			SetDefaultKeybindings();
			SetDefaultVideoSettings();
			SetDefaultAudioSettings();
			SetDefaultControlSettings();
			config.Save(SETTINGS_FILE_PATH);
		}
		else
		{
			config.Load(SETTINGS_FILE_PATH);
		}

		EnsureVideoDefaults();
		EnsureAudioDefaults();
		EnsureControlDefaults();
		Instance = this;
		ApplyVideoSettings();
		ApplyKeybindings();
	}

	private void SetDefaultKeybindings()
	{
		foreach (var kvp in DefaultKeybindings)
			config.SetValue("keybinding", kvp.Key, kvp.Value);
	}

	private void SetDefaultVideoSettings()
	{
		config.SetValue("video", "fps",             DefaultFps);
		config.SetValue("video", "mode",            DefaultMode);
		config.SetValue("video", "vsync",           DefaultVsync);
		config.SetValue("video", "fxaa",            DefaultFxaa);
		config.SetValue("video", "taa",             DefaultTaa);
		config.SetValue("video", "msaa",            DefaultMsaa);
		config.SetValue("video", "resolution",      DefaultResolution);
		config.SetValue("video", "render_scale",    DefaultRenderScale);
		config.SetValue("video", "upscaler",        DefaultUpscaler);
		config.SetValue("video", "show_fps_meter",  DefaultShowFpsMeter);
		config.SetValue("video", "show_ship_model", DefaultShowShipModel);
		config.SetValue("video", "cockpit_fov",     DefaultCockpitFov);
	}

	private void SetDefaultAudioSettings()
	{
		config.SetValue("audio", "general_volume", DefaultGeneralVolume);
		config.SetValue("audio", "music_volume",   DefaultMusicVolume);
		config.SetValue("audio", "sfx_volume",     DefaultSfxVolume);
	}

	private void SetDefaultControlSettings()
	{
		config.SetValue("controls", "relative_mouse",    DefaultRelativeMouse);
		config.SetValue("controls", "aim_sensitivity",   DefaultAimSensitivity);
		config.SetValue("controls", "aim_deadzone",      DefaultAimDeadzone);
		config.SetValue("controls", "auto_center_speed", DefaultAutoCenterSpeed);
	}

	private Dictionary<string, Variant> LoadSection(string section)
	{
		Dictionary<string, Variant> result = new();
		if (!config.HasSection(section))
			return result;

		foreach (string key in config.GetSectionKeys(section))
			result[key] = config.GetValue(section, key);

		return result;
	}

	private void SaveKey(string section, string key, Variant value)
	{
		config.SetValue(section, key, value);
		config.Save(SETTINGS_FILE_PATH);
	}
}
