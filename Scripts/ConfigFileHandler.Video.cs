using Godot;
using System.Collections.Generic;

public partial class ConfigFileHandler
{
	private const int    DefaultFps           = 90;
	private const string DefaultMode          = "fullscreen";
	private const string DefaultVsync         = "disabled";
	private const bool   DefaultFxaa          = true;
	private const bool   DefaultTaa           = false;
	private const string DefaultMsaa          = "4x";
	private const string DefaultResolution    = "1920x1080";
	private const int    DefaultRenderScale   = 100;
	private const string DefaultUpscaler      = "bilinear";
	private const bool   DefaultShowFpsMeter  = true;
	private const bool   DefaultShowShipModel = true;
	private const int    DefaultCockpitFov    = 70;
	private const bool   DefaultSsao          = true;
	private const bool   DefaultVolumetricFog = true;
	public const int     MinCockpitFov        = 60;
	public const int     MaxCockpitFov        = 80;

	public void SaveVideoSettings(string key, Variant value)
	{
		SaveKey("video", key, value);
	}

	public Dictionary<string, Variant> LoadVideoSettings()
	{
		return LoadSection("video");
	}

	public void ResetVideoSettings()
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
		config.SetValue("video", "ssao",            DefaultSsao);
		config.SetValue("video", "volumetric_fog",  DefaultVolumetricFog);
		config.Save(SETTINGS_FILE_PATH);
		ApplyVideoSettings();
	}

	public bool GetShowFpsMeter()
	{
		return config.HasSectionKey("video", "show_fps_meter")
			? (bool)config.GetValue("video", "show_fps_meter")
			: DefaultShowFpsMeter;
	}

	public bool GetShowShipModel()
	{
		return config.HasSectionKey("video", "show_ship_model")
			? (bool)config.GetValue("video", "show_ship_model")
			: DefaultShowShipModel;
	}

	public int GetCockpitFov()
	{
		return config.HasSectionKey("video", "cockpit_fov")
			? Mathf.Clamp((int)config.GetValue("video", "cockpit_fov"), MinCockpitFov, MaxCockpitFov)
			: DefaultCockpitFov;
	}

	private void EnsureVideoDefaults()
	{
		bool changed = false;
		void Ensure(string key, Variant value)
		{
			if (config.HasSectionKey("video", key)) return;
			config.SetValue("video", key, value);
			changed = true;
		}
		Ensure("fps",             DefaultFps);
		Ensure("mode",            DefaultMode);
		Ensure("vsync",           DefaultVsync);
		Ensure("fxaa",            DefaultFxaa);
		Ensure("taa",             DefaultTaa);
		Ensure("msaa",            DefaultMsaa);
		Ensure("resolution",      DefaultResolution);
		Ensure("render_scale",    DefaultRenderScale);
		Ensure("upscaler",        DefaultUpscaler);
		Ensure("show_fps_meter",  DefaultShowFpsMeter);
		Ensure("show_ship_model", DefaultShowShipModel);
		Ensure("cockpit_fov",     DefaultCockpitFov);
		Ensure("ssao",            DefaultSsao);
		Ensure("volumetric_fog",  DefaultVolumetricFog);
		if (changed) config.Save(SETTINGS_FILE_PATH);
	}

	public static DisplayServer.VSyncMode ParseVsyncMode(string value) => value.ToLowerInvariant() switch
	{
		"adaptive"          => DisplayServer.VSyncMode.Adaptive,
		"enabled" or "true" => DisplayServer.VSyncMode.Enabled,
		_                   => DisplayServer.VSyncMode.Disabled,
	};

	public static Vector2I ParseResolution(string value) => value switch
	{
		"854x480"   => new Vector2I(854,  480),
		"1280x720"  => new Vector2I(1280, 720),
		"2560x1440" => new Vector2I(2560, 1440),
		"3840x2160" => new Vector2I(3840, 2160),
		_           => new Vector2I(1920, 1080),
	};

	// FXAA is off whatever the setting says, as of the last ApplyVideoSettings.
	public bool FxaaBlocked { get; private set; }

	public void ApplyVideoSettings()
	{
		var videoSettings = LoadVideoSettings();
		var viewport = GetViewport();

		// FPS
		int fps = videoSettings.ContainsKey("fps") ? (int)videoSettings["fps"] : DefaultFps;
		Engine.MaxFps = fps;

		// VSync
		string vsync = videoSettings.ContainsKey("vsync") ? videoSettings["vsync"].ToString() : DefaultVsync;
		DisplayServer.WindowSetVsyncMode(ParseVsyncMode(vsync));

		string upscaler = videoSettings.ContainsKey("upscaler") ? videoSettings["upscaler"].ToString() : DefaultUpscaler;

		// TAA. FSR 2.2 does its own, and Godot warns whenever both are on, even for a moment: so TAA
		// stays off under FSR 2.2, goes off before the upscaler changes and comes on only after.
		bool taa = videoSettings.ContainsKey("taa") && (bool)videoSettings["taa"] && upscaler != "fsr2";
		if (!taa) viewport.UseTaa = false;

		// MSAA
		string msaa = videoSettings.ContainsKey("msaa") ? videoSettings["msaa"].ToString() : DefaultMsaa;
		int msaaIndex = msaa switch { "2x" => 1, "4x" => 2, "8x" => 3, _ => 0 };
		viewport.Msaa3D = (Viewport.Msaa)msaaIndex;

		// 3D render scale + upscaler (works in every window mode)
		int scale = videoSettings.ContainsKey("render_scale") ? (int)videoSettings["render_scale"] : DefaultRenderScale;
		viewport.Scaling3DScale = Mathf.Clamp(scale, 50, 100) / 100f;
		viewport.Scaling3DMode = upscaler switch
		{
			"fsr1" => Viewport.Scaling3DModeEnum.Fsr,
			"fsr2" => Viewport.Scaling3DModeEnum.Fsr2,
			_      => Viewport.Scaling3DModeEnum.Bilinear,
		};
		viewport.UseTaa = taa;

		// FXAA. Godot fails every 2D draw with FXAA, 2D MSAA and a bilinear downscale settings together.
		// so FXAA gives way in just that combination.
		bool fxaa = videoSettings.ContainsKey("fxaa") && (bool)videoSettings["fxaa"];
		FxaaBlocked = viewport.Msaa2D != Viewport.Msaa.Disabled
			&& viewport.Scaling3DMode == Viewport.Scaling3DModeEnum.Bilinear
			&& viewport.Scaling3DScale < 1f;
		viewport.ScreenSpaceAA = fxaa && !FxaaBlocked ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;

		// Window mode: "fullscreen" is exclusive fullscreen, "borderless" is a
		// borderless fullscreen window. The window resolution only exists in
		// windowed mode, where the window is also re-centered on its screen.
		string mode = videoSettings.ContainsKey("mode") ? videoSettings["mode"].ToString() : DefaultMode;
		switch (mode)
		{
			case "windowed":
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
				DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
				string res = videoSettings.ContainsKey("resolution") ? videoSettings["resolution"].ToString() : DefaultResolution;
				SetWindowedResolution(ParseResolution(res));
				break;
			case "borderless":
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
				break;
			default:
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
				break;
		}

		foreach (Node node in GetTree().Root.FindChildren("*", nameof(WorldEnvironment), true, false))
			ApplyEnvironmentSettings((WorldEnvironment)node);
	}

	// Levels switch SSAO and volumetric fog on where they want them; the settings can only switch
	// them off. Every load of a scene shares the Environment in its file, so each WorldEnvironment
	// works on its own copy and keeps what its level authored beside it.
	private const string AuthoredSsao = "authored_ssao";
	private const string AuthoredVolumetricFog = "authored_volumetric_fog";

	private void OnNodeAdded(Node node)
	{
		if (node is WorldEnvironment worldEnv) ApplyEnvironmentSettings(worldEnv);
	}

	private void ApplyEnvironmentSettings(WorldEnvironment worldEnv)
	{
		Environment env = worldEnv.Environment;
		if (env == null) return;

		if (!worldEnv.HasMeta(AuthoredSsao))
		{
			worldEnv.SetMeta(AuthoredSsao, env.SsaoEnabled);
			worldEnv.SetMeta(AuthoredVolumetricFog, env.VolumetricFogEnabled);
			env = (Environment)env.Duplicate();
			worldEnv.Environment = env;
		}

		var videoSettings = LoadVideoSettings();
		env.SsaoEnabled = worldEnv.GetMeta(AuthoredSsao).AsBool() && videoSettings["ssao"].AsBool();
		env.VolumetricFogEnabled = worldEnv.GetMeta(AuthoredVolumetricFog).AsBool()
			&& videoSettings["volumetric_fog"].AsBool();
	}

	// Resizes the window and re-centers it on its current screen.
	private static void SetWindowedResolution(Vector2I size)
	{
		DisplayServer.WindowSetSize(size);
		int screen = DisplayServer.WindowGetCurrentScreen();
		Vector2I screenPos  = DisplayServer.ScreenGetPosition(screen);
		Vector2I screenSize = DisplayServer.ScreenGetSize(screen);
		DisplayServer.WindowSetPosition(screenPos + (screenSize - size) / 2);
	}
}
