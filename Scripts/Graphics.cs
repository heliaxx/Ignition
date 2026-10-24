using Godot;
using System;

public partial class Graphics : Control
{
	[ExportGroup("Node References")]
	[Export] private OptionButton fpsDropdown;
	[Export] private OptionButton modeDropdown;
	[Export] private OptionButton vsyncDropdown;
	[Export] private CheckBox fxaaCheck;
	[Export] private CheckBox taaCheck;
	[Export] private OptionButton msaaDropdown;
	[Export] private OptionButton resolutionDropdown;
	[Export] private OptionButton renderScaleDropdown;
	[Export] private OptionButton upscalerDropdown;
	[Export] private OptionButton presetDropdown;
	[Export] private CheckBox ssaoCheck;
	[Export] private CheckBox volumetricFogCheck;
	[Export] private CheckBox fpsMeterCheck;
	[Export] private CheckBox shipModelCheck;
	[Export] private HSlider cockpitFovSlider;
	[Export] private Label cockpitFovValue;
	[Export] private Button backBtn;
	[Export] private Label hintLabel;

	private ConfigFileHandler configHandler;
	private readonly int?[] fpsOptions = { null, 30, 60, 90, 144, 240 }; // null = Unlimited
	private readonly int[] renderScaleOptions = { 100, 75, 67, 50 };     // percent

	// What each quality preset sets. The rest are preferences, not strictly quality.
	private readonly record struct Preset(string Name, string Msaa, bool Fxaa, bool Taa, int RenderScale,
		string Upscaler, bool Ssao, bool VolumetricFog);

	private static readonly Preset[] Presets =
	{
		new("Ultra Low",  "2x",       false, true,  50,  "fsr1",     false, false),
		new("Low",        "disabled", false, false, 67,  "fsr2",     false, false),
		new("Medium",     "disabled", true,  false, 100, "fsr2",     false, true),
		new("High",       "disabled", true,  false, 100, "fsr2",     true,  true),
		new("Ultra High", "8x",       false, true,  100, "bilinear", true,  true),
	};

	public override void _Ready()
	{
		fpsDropdown ??= GetNode<OptionButton>("Center/Rows/FPSLimitRow/FPSDropdown");
		modeDropdown ??= GetNode<OptionButton>("Center/Rows/ModeRow/ModeDropdown");
		vsyncDropdown ??= GetNode<OptionButton>("Center/Rows/VsyncRow/VsyncDropdown");
		fxaaCheck ??= GetNode<CheckBox>("Center/Rows/FXAARow/FXAACheckSlot/FXAACheck");
		taaCheck ??= GetNode<CheckBox>("Center/Rows/TAARow/TAACheckSlot/TAACheck");
		msaaDropdown ??= GetNode<OptionButton>("Center/Rows/MSAARow/MSAADropdown");
		resolutionDropdown ??= GetNode<OptionButton>("Center/Rows/ResolutionRow/ResolutionDropdown");
		renderScaleDropdown ??= GetNode<OptionButton>("Center/Rows/RenderScaleRow/RenderScaleDropdown");
		upscalerDropdown ??= GetNode<OptionButton>("Center/Rows/UpscalerRow/UpscalerDropdown");
		presetDropdown ??= GetNode<OptionButton>("Center/Rows/PresetRow/PresetDropdown");
		ssaoCheck ??= GetNode<CheckBox>("Center/Rows/SSAORow/SSAOCheckSlot/SSAOCheck");
		volumetricFogCheck ??= GetNode<CheckBox>("Center/Rows/VolumetricFogRow/VolumetricFogCheckSlot/VolumetricFogCheck");
		fpsMeterCheck ??= GetNode<CheckBox>("Center/Rows/FPSMeterRow/FPSMeterCheckSlot/FPSMeterCheck");
		shipModelCheck ??= GetNode<CheckBox>("Center/Rows/ShipModelRow/ShipModelCheckSlot/ShipModelCheck");
		cockpitFovSlider ??= GetNode<HSlider>("Center/Rows/CockpitFovRow/CockpitFovControl/CockpitFovSlider");
		cockpitFovValue ??= GetNode<Label>("Center/Rows/CockpitFovRow/CockpitFovControl/CockpitFovValue");
		backBtn ??= GetNode<Button>("HBoxContainer/BackBtn");
		hintLabel ??= GetNode<Label>("HintLabel");

		configHandler = GetTree().Root.GetNode<ConfigFileHandler>("/root/ConfigFileHandler");

		InitializeFPSDropdown();
		InitializeModeDropdown();
		InitializeVsyncDropdown();
		InitializeMSAADropdown();
		InitializeResolutionDropdown();
		InitializeRenderScaleDropdown();
		InitializeUpscalerDropdown();
		InitializePresetDropdown();
		InitializeCockpitFovSlider();

		LoadCurrentSettings();
		SetupHints();
		MenuUtils.AttachButtonSounds(this);
	}

	// Esc closes the options, same as BACK.
	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("menu"))
		{
			GetViewport().SetInputAsHandled();
			_on_back_btn_pressed();
		}
	}

	// Shows a short explanation above the menu while an option is hovered.
	private void SetupHints()
	{
		RegisterHint(presetDropdown, "Sets anti-aliasing, render scale and effects together. Changing any of them yourself makes it Custom.");
		RegisterHint(fpsDropdown, "Caps rendered FPS. Lower values reduce GPU load.");
		RegisterHint(modeDropdown, "Windowed: resizable window. Borderless: fullscreen window, fast alt-tabbing. Fullscreen: marginally lower latency and better performance.");
		RegisterHint(vsyncDropdown, "Syncs rendered frames to the monitor refresh rate to prevent tearing. Enabled adds a little input latency; Adaptive only syncs while the frame rate keeps up.");
		RegisterHint(fxaaCheck, () => configHandler.FxaaBlocked
			? "FXAA is unavailable with the Bilinear upscaler below native render scale. Switch to FSR or render at 100% to use it."
			: "Fast approximate anti-aliasing. Cheaply smooths jagged edges at the cost of slight blur.");
		RegisterHint(taaCheck, () => upscalerDropdown.Selected == 2
			? "FSR 2.2 does its own temporal anti-aliasing, so TAA is ignored while it is the upscaler."
			: "Temporal anti-aliasing. Very smooth image, but can introduce ghosting trails.");
		RegisterHint(msaaDropdown, "Multisample anti-aliasing. High-quality geometry edges; increasingly expensive at 4 and 8 samples per pixel.");
		RegisterHint(resolutionDropdown, "Window size in pixels. Only relevant in Windowed mode.");
		RegisterHint(renderScaleDropdown, "Percentage of the native resolution to render at. Lower values provide performance gain at the cost of image quality.");
		RegisterHint(upscalerDropdown, "How the image is scaled up from a lower render scale. Bilinear: fast but blurry. FSR 1.0: sharper, does nothing at 100%. FSR 2.2: sharpest and anti-aliases even at 100%, but costs the most and can leave trails behind fast motion.");
		RegisterHint(ssaoCheck, "Screen-space ambient occlusion. Shades creases and contact points for depth; costs GPU time.");
		RegisterHint(volumetricFogCheck, "Volumetric fog lit by the sun and lights. One of the most expensive effects; turning it off gives the biggest gain.");
		RegisterHint(fpsMeterCheck, "Shows the frame rate during gameplay.");
		RegisterHint(shipModelCheck, "Photo mode when off: hides the player ship for a completely clean screen.");
		RegisterHint(cockpitFovSlider, "Field of view from the cockpit, in degrees. Wider shows more around the ship; narrower makes what is ahead larger.");
	}

	private void RegisterHint(Control control, string text) => RegisterHint(control, () => text);

	private void RegisterHint(Control control, Func<string> text)
	{
		control.MouseEntered += () => hintLabel.Text = text();
		// Only clear if another control's hint hasn't replaced this one already.
		control.MouseExited += () => { if (hintLabel.Text == text()) hintLabel.Text = ""; };
	}

	private void InitializeFPSDropdown()
	{
		fpsDropdown.Clear();
		fpsDropdown.AddItem("Unlimited");
		for (int i = 1; i < fpsOptions.Length; i++)
			fpsDropdown.AddItem($"{fpsOptions[i]} FPS");
	}

	private void InitializeModeDropdown()
	{
		modeDropdown.Clear();
		modeDropdown.AddItem("Windowed");
		modeDropdown.AddItem("Borderless");
		modeDropdown.AddItem("Fullscreen");
	}

	private void InitializeVsyncDropdown()
	{
		vsyncDropdown.Clear();
		vsyncDropdown.AddItem("Disabled");
		vsyncDropdown.AddItem("Enabled");
		vsyncDropdown.AddItem("Adaptive");
	}

	private void InitializeMSAADropdown()
	{
		msaaDropdown.Clear();
		msaaDropdown.AddItem("Disabled");
		msaaDropdown.AddItem("2x");
		msaaDropdown.AddItem("4x");
		msaaDropdown.AddItem("8x");
	}

	private void InitializeResolutionDropdown()
	{
		resolutionDropdown.Clear();
		resolutionDropdown.AddItem("854x480");
		resolutionDropdown.AddItem("1280x720");
		resolutionDropdown.AddItem("1920x1080");
		resolutionDropdown.AddItem("2560x1440");
		resolutionDropdown.AddItem("3840x2160");
	}

	private void InitializeRenderScaleDropdown()
	{
		renderScaleDropdown.Clear();
		foreach (int scale in renderScaleOptions)
			renderScaleDropdown.AddItem($"{scale}%");
	}

	private void InitializeUpscalerDropdown()
	{
		upscalerDropdown.Clear();
		upscalerDropdown.AddItem("Bilinear");
		upscalerDropdown.AddItem("FSR 1.0");
		upscalerDropdown.AddItem("FSR 2.2");
	}

	private void InitializePresetDropdown()
	{
		presetDropdown.Clear();
		foreach (Preset preset in Presets)
			presetDropdown.AddItem(preset.Name);
		// Shown, never picked: it names settings that match no preset.
		presetDropdown.AddItem("Custom");
		presetDropdown.SetItemDisabled(Presets.Length, true);
	}

	private void InitializeCockpitFovSlider()
	{
		cockpitFovSlider.MinValue = ConfigFileHandler.MinCockpitFov;
		cockpitFovSlider.MaxValue = ConfigFileHandler.MaxCockpitFov;
		cockpitFovSlider.Step = 1;
	}

	private void LoadCurrentSettings()
	{
		var videoSettings = configHandler.LoadVideoSettings();

		int fps = videoSettings.ContainsKey("fps") ? (int)videoSettings["fps"] : 90;
		for (int i = 0; i < fpsOptions.Length; i++)
		{
			if ((fpsOptions[i] ?? 0) == fps)
			{
				fpsDropdown.Selected = i;
				break;
			}
		}

		string vsync = videoSettings.ContainsKey("vsync") ? videoSettings["vsync"].ToString().ToLowerInvariant() : "disabled";
		vsyncDropdown.Selected = vsync switch { "enabled" or "true" => 1, "adaptive" => 2, _ => 0 };

		// Without signals: the toggle handlers re-apply every video setting, and re-setting vsync
		// rebuilds the swap chain, which flashes the screen.
		fxaaCheck.SetPressedNoSignal(videoSettings.ContainsKey("fxaa") && (bool)videoSettings["fxaa"]);
		taaCheck.SetPressedNoSignal(videoSettings.ContainsKey("taa") && (bool)videoSettings["taa"]);
		ssaoCheck.SetPressedNoSignal(videoSettings["ssao"].AsBool());
		volumetricFogCheck.SetPressedNoSignal(videoSettings["volumetric_fog"].AsBool());

		string mode = videoSettings.ContainsKey("mode") ? videoSettings["mode"].ToString() : "fullscreen";
		modeDropdown.Selected = mode switch { "windowed" => 0, "borderless" => 1, _ => 2 };

		string msaa = videoSettings.ContainsKey("msaa") ? videoSettings["msaa"].ToString() : "4x";
		msaaDropdown.Selected = msaa switch { "2x" => 1, "4x" => 2, "8x" => 3, _ => 0 };

		string res = videoSettings.ContainsKey("resolution") ? videoSettings["resolution"].ToString() : "1920x1080";
		resolutionDropdown.Selected = res switch
		{
			"854x480"   => 0,
			"1280x720"  => 1,
			"2560x1440" => 3,
			"3840x2160" => 4,
			_           => 2
		};

		int scale = videoSettings.ContainsKey("render_scale") ? (int)videoSettings["render_scale"] : 100;
		int scaleIndex = Array.IndexOf(renderScaleOptions, scale);
		renderScaleDropdown.Selected = scaleIndex >= 0 ? scaleIndex : 0;

		string upscaler = videoSettings.ContainsKey("upscaler") ? videoSettings["upscaler"].ToString() : "bilinear";
		upscalerDropdown.Selected = upscaler switch { "fsr1" => 1, "fsr2" => 2, _ => 0 };

		fpsMeterCheck.ButtonPressed = configHandler.GetShowFpsMeter();
		shipModelCheck.ButtonPressed = configHandler.GetShowShipModel();
		cockpitFovSlider.Value = configHandler.GetCockpitFov();
		cockpitFovValue.Text = $"{configHandler.GetCockpitFov()}°";

		UpdateResolutionEnabled();
		UpdateFxaaEnabled();
		UpdateUpscalerDependents();
		UpdatePresetSelection();
	}

	// Custom when the settings match no preset, as after changing one of them by hand.
	private void UpdatePresetSelection()
	{
		var video = configHandler.LoadVideoSettings();
		int match = Array.FindIndex(Presets, p =>
			video["msaa"].AsString() == p.Msaa
			&& video["fxaa"].AsBool() == p.Fxaa
			&& video["taa"].AsBool() == p.Taa
			&& video["render_scale"].AsInt32() == p.RenderScale
			&& video["upscaler"].AsString() == p.Upscaler
			&& video["ssao"].AsBool() == p.Ssao
			&& video["volumetric_fog"].AsBool() == p.VolumetricFog);
		presetDropdown.Selected = match >= 0 ? match : Presets.Length;
	}

	private void _on_preset_dropdown_item_selected(int index)
	{
		Preset preset = Presets[index];
		configHandler.SaveVideoSettings("msaa", preset.Msaa);
		configHandler.SaveVideoSettings("fxaa", preset.Fxaa);
		configHandler.SaveVideoSettings("taa", preset.Taa);
		configHandler.SaveVideoSettings("render_scale", preset.RenderScale);
		configHandler.SaveVideoSettings("upscaler", preset.Upscaler);
		configHandler.SaveVideoSettings("ssao", preset.Ssao);
		configHandler.SaveVideoSettings("volumetric_fog", preset.VolumetricFog);
		configHandler.ApplyVideoSettings();
		LoadCurrentSettings();
	}

	// The window resolution only takes effect in windowed mode.
	private void UpdateResolutionEnabled()
	{
		resolutionDropdown.Disabled = modeDropdown.Selected != 0;
	}

	private void UpdateFxaaEnabled()
	{
		fxaaCheck.Disabled = configHandler.FxaaBlocked;
	}

	// FSR 2.2 brings its own temporal anti-aliasing, so TAA does nothing beside it. FSR 1.0 only
	// upscales, so at 100% it does nothing at all; FSR 2.2 still anti-aliases there.
	private void UpdateUpscalerDependents()
	{
		taaCheck.Disabled = upscalerDropdown.Selected == 2;
		upscalerDropdown.SetItemDisabled(1, renderScaleOptions[renderScaleDropdown.Selected] == 100);
	}

	private void _on_fps_dropdown_item_selected(int index)
	{
		configHandler.SaveVideoSettings("fps", fpsOptions[index] ?? 0);
		configHandler.ApplyVideoSettings();
	}

	private void _on_mode_dropdown_item_selected(int index)
	{
		string mode = index switch { 0 => "windowed", 1 => "borderless", _ => "fullscreen" };
		configHandler.SaveVideoSettings("mode", mode);
		configHandler.ApplyVideoSettings();
		UpdateResolutionEnabled();
	}

	private void _on_vsync_dropdown_item_selected(int index)
	{
		string vsync = index switch { 1 => "enabled", 2 => "adaptive", _ => "disabled" };
		configHandler.SaveVideoSettings("vsync", vsync);
		configHandler.ApplyVideoSettings();
	}

	private void _on_fxaa_check_toggled(bool toggled)
	{
		configHandler.SaveVideoSettings("fxaa", toggled);
		configHandler.ApplyVideoSettings();
		UpdatePresetSelection();
	}

	private void _on_taa_check_toggled(bool toggled)
	{
		configHandler.SaveVideoSettings("taa", toggled);
		configHandler.ApplyVideoSettings();
		UpdatePresetSelection();
	}

	private void _on_msaa_dropdown_item_selected(int index)
	{
		string msaaStr = index switch { 1 => "2x", 2 => "4x", 3 => "8x", _ => "disabled" };
		configHandler.SaveVideoSettings("msaa", msaaStr);
		configHandler.ApplyVideoSettings();
		UpdatePresetSelection();
	}

	private void _on_resolution_dropdown_item_selected(int index)
	{
		string resStr = index switch
		{
			0 => "854x480",
			1 => "1280x720",
			2 => "1920x1080",
			3 => "2560x1440",
			4 => "3840x2160",
			_ => "1920x1080"
		};
		configHandler.SaveVideoSettings("resolution", resStr);
		configHandler.ApplyVideoSettings();
	}

	private void _on_render_scale_dropdown_item_selected(int index)
	{
		configHandler.SaveVideoSettings("render_scale", renderScaleOptions[index]);
		configHandler.ApplyVideoSettings();
		UpdateFxaaEnabled();
		UpdateUpscalerDependents();
		UpdatePresetSelection();
	}

	private void _on_upscaler_dropdown_item_selected(int index)
	{
		string upscaler = index switch { 1 => "fsr1", 2 => "fsr2", _ => "bilinear" };
		configHandler.SaveVideoSettings("upscaler", upscaler);
		configHandler.ApplyVideoSettings();
		UpdateFxaaEnabled();
		UpdateUpscalerDependents();
		UpdatePresetSelection();
	}

	private void _on_ssao_check_toggled(bool toggled)
	{
		configHandler.SaveVideoSettings("ssao", toggled);
		configHandler.ApplyVideoSettings();
		UpdatePresetSelection();
	}

	private void _on_volumetric_fog_check_toggled(bool toggled)
	{
		configHandler.SaveVideoSettings("volumetric_fog", toggled);
		configHandler.ApplyVideoSettings();
		UpdatePresetSelection();
	}

	private void _on_fps_meter_check_toggled(bool toggled)
	{
		configHandler.SaveVideoSettings("show_fps_meter", toggled);
	}

	private void _on_ship_model_check_toggled(bool toggled)
	{
		configHandler.SaveVideoSettings("show_ship_model", toggled);
	}

	private void _on_cockpit_fov_slider_value_changed(double value)
	{
		configHandler.SaveVideoSettings("cockpit_fov", (int)value);
		cockpitFovValue.Text = $"{(int)value}°";
	}

	private void _on_back_btn_pressed()
	{
		GetParent<OptionsMenu>().Close();
	}

	private void _on_reset_btn_pressed()
	{
		configHandler.ResetVideoSettings();
		LoadCurrentSettings();
	}
}
