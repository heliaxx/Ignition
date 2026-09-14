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
	[Export] private CheckBox fpsMeterCheck;
	[Export] private CheckBox shipModelCheck;
	[Export] private HSlider cockpitFovSlider;
	[Export] private Label cockpitFovValue;
	[Export] private Button backBtn;
	[Export] private Label hintLabel;

	private ConfigFileHandler configHandler;
	private readonly int?[] fpsOptions = { null, 30, 60, 90, 144, 240 }; // null = Unlimited
	private readonly int[] renderScaleOptions = { 100, 75, 67, 50 };     // percent

	public override void _Ready()
	{
		fpsDropdown ??= GetNode<OptionButton>("Menu/Options/FPSDropdown");
		modeDropdown ??= GetNode<OptionButton>("Menu/Options/ModeDropdown");
		vsyncDropdown ??= GetNode<OptionButton>("Menu/Options/VsyncDropdown");
		fxaaCheck ??= GetNode<CheckBox>("Menu/Options/FXAACheck");
		taaCheck ??= GetNode<CheckBox>("Menu/Options/TAACheck");
		msaaDropdown ??= GetNode<OptionButton>("Menu/Options/MSAADropdown");
		resolutionDropdown ??= GetNode<OptionButton>("Menu/Options/ResolutionDropdown");
		renderScaleDropdown ??= GetNode<OptionButton>("Menu/Options/RenderScaleDropdown");
		upscalerDropdown ??= GetNode<OptionButton>("Menu/Options/UpscalerDropdown");
		fpsMeterCheck ??= GetNode<CheckBox>("Menu/Options/FPSMeterCheck");
		shipModelCheck ??= GetNode<CheckBox>("Menu/Options/ShipModelCheck");
		cockpitFovSlider ??= GetNode<HSlider>("Menu/Options/CockpitFovRow/CockpitFovSlider");
		cockpitFovValue ??= GetNode<Label>("Menu/Options/CockpitFovRow/CockpitFovValue");
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
		InitializeCockpitFovSlider();

		FixDropdownPopup(fpsDropdown);
		FixDropdownPopup(modeDropdown);
		FixDropdownPopup(vsyncDropdown);
		FixDropdownPopup(msaaDropdown);
		FixDropdownPopup(resolutionDropdown);
		FixDropdownPopup(renderScaleDropdown);
		FixDropdownPopup(upscalerDropdown);

		LoadCurrentSettings();
		SetupHints();
		MenuUtils.AttachButtonSounds(this);
	}

	// Esc backs out to the main menu, same as the pause menu's action in-game.
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
		RegisterHint(fpsDropdown, "Caps rendered FPS. Lower values reduce GPU load.");
		RegisterHint(modeDropdown, "Windowed: resizable window. Borderless: fullscreen window, fast alt-tabbing. Fullscreen: marginally lower latency and better performance.");
		RegisterHint(vsyncDropdown, "Syncs rendered frames to the monitor refresh rate to prevent tearing. Enabled adds a little input latency; Adaptive only syncs while the frame rate keeps up.");
		RegisterHint(fxaaCheck, "Fast approximate anti-aliasing. Cheaply smooths jagged edges at the cost of slight blur.");
		RegisterHint(taaCheck, "Temporal anti-aliasing. Very smooth image, but can introduce ghosting trails.");
		RegisterHint(msaaDropdown, "Multisample anti-aliasing. High-quality geometry edges; increasingly expensive at 4 and 8 samples per pixel.");
		RegisterHint(resolutionDropdown, "Window size in pixels. Only relevant in Windowed mode.");
		RegisterHint(renderScaleDropdown, "Percentage of the native resolution to render at. Lower values provide performance gain at the cost of image quality.");
		RegisterHint(upscalerDropdown, "Upscaling method. Bilinear is fast but blurry; FSR 1.0 and 2.2 sharper but more expensive.");
		RegisterHint(fpsMeterCheck, "Shows the frame rate during gameplay.");
		RegisterHint(shipModelCheck, "Photo mode when off: hides the player ship for a completely clean screen.");
		RegisterHint(cockpitFovSlider, "Field of view from the cockpit, in degrees. Wider shows more around the ship; narrower makes what is ahead larger.");
	}

	private void RegisterHint(Control control, string text)
	{
		control.MouseEntered += () => hintLabel.Text = text;
		// Only clear if another control's hint hasn't replaced this one already.
		control.MouseExited += () => { if (hintLabel.Text == text) hintLabel.Text = ""; };
	}

	private void FixDropdownPopup(OptionButton dropdown)
	{
		var popup = dropdown.GetPopup();
		popup.AddThemeFontSizeOverride("font_size", (int)dropdown.GetThemeFontSize("font_size"));
		if (dropdown.HasThemeFontOverride("font"))
			popup.AddThemeFontOverride("font", dropdown.GetThemeFont("font"));
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

	private void InitializeCockpitFovSlider()
	{
		cockpitFovSlider.MinValue = ConfigFileHandler.MinCockpitFov;
		cockpitFovSlider.MaxValue = ConfigFileHandler.MaxCockpitFov;
		cockpitFovSlider.Step = 1;
	}

	private void LoadCurrentSettings()
	{
		configHandler.ApplyVideoSettings();

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

		fxaaCheck.ButtonPressed = videoSettings.ContainsKey("fxaa") && (bool)videoSettings["fxaa"];
		taaCheck.ButtonPressed  = videoSettings.ContainsKey("taa")  && (bool)videoSettings["taa"];

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
	}

	// The window resolution only takes effect in windowed mode.
	private void UpdateResolutionEnabled()
	{
		resolutionDropdown.Disabled = modeDropdown.Selected != 0;
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
	}

	private void _on_taa_check_toggled(bool toggled)
	{
		configHandler.SaveVideoSettings("taa", toggled);
		configHandler.ApplyVideoSettings();
	}

	private void _on_msaa_dropdown_item_selected(int index)
	{
		string msaaStr = index switch { 1 => "2x", 2 => "4x", 3 => "8x", _ => "disabled" };
		configHandler.SaveVideoSettings("msaa", msaaStr);
		configHandler.ApplyVideoSettings();
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
	}

	private void _on_upscaler_dropdown_item_selected(int index)
	{
		string upscaler = index switch { 1 => "fsr1", 2 => "fsr2", _ => "bilinear" };
		configHandler.SaveVideoSettings("upscaler", upscaler);
		configHandler.ApplyVideoSettings();
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
		GetTree().ChangeSceneToFile("res://Scenes/Menu.tscn");
	}

	private void _on_reset_btn_pressed()
	{
		configHandler.ResetVideoSettings();
		LoadCurrentSettings();
	}
}
