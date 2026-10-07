using Godot;
using System;
using System.Collections.Generic;

public partial class Controls : Control
{
	[Export]
	public PackedScene InputButtonScene;

	private VBoxContainer ActionList;
	private bool IsRemapping = false;
	private string ActionToRemap = null;
	private Button RemappingButton = null;
	private AudioStreamPlayer _menuClick;
	private HSlider aimSensitivitySlider;
	private HSlider aimDeadzoneSlider;
	private HSlider aimPowerCurveSlider;
	private HSlider autoCenterSpeedSlider;
	private Label aimSensitivityValue;
	private Label aimDeadzoneValue;
	private Label aimPowerCurveValue;
	private Label autoCenterSpeedValue;
	private CheckBox aimWidgetCheck;
	private CheckBox vectorIndicatorCheck;
	private OptionButton flightAssistToggleDropdown;
	private const int FlightAssistToggleOff = 0;
	private const int FlightAssistTogglePartial = 1;
	private OptionButton flightAssistModeDropdown;
	private const int FlightAssistModeThrust = 0;
	private const int FlightAssistModeThrottle = 1;
	private const string Sections = "SettingsPanel/Margin/Sections/";
	private const int KeybindRowBaseLeftMargin = 0;
	private const int KeybindRowHoverLeftMargin = 20;
	private static readonly Color KeybindRowNormalTextColor = new Color(0.894f, 0.718f, 0.337f, 1f);
	private static readonly Color KeybindRowHoverTextColor = new Color(0f, 0f, 0f, 1f);

	private Dictionary<string, string> InputActions = new()
	{
		{ "thrust_forward", "Thrust forward" },
		{ "thrust_backward", "Thrust backward" },
		{ "roll_right", "Roll right" },
		{ "roll_left", "Roll left" },
		{ "strafe_right", "Strafe right" },
		{ "strafe_left", "Strafe left" },
		{ "strafe_up", "Strafe up" },
		{ "strafe_down", "Strafe down" },
		{ "boost", "Boost" },
		{ "stop", "Precise stop" },
		{ "flight_assist", "Flight assist toggle" },
		{ "relative_mouse", "Relative mouse on/off" },
		{ "light", "Light" },
		{ "camera_switch", "Switch camera" },
		{ "target_cycle", "Select target ahead" },
		{ "target_cycle_all", "Cycle targets" },
		{ "free_look", "Look around" },
		{ "scoreboard", "Scoreboard" }
	};

	public override void _Ready()
	{
		InputButtonScene ??= GD.Load<PackedScene>("res://Scenes/InputButton.tscn");
		ActionList = GetNode<VBoxContainer>(Sections + "KeyboardSettings/KeybindScroll/ActionList");
		aimSensitivitySlider = GetNode<HSlider>(Sections + "MouseSettings/AimSensitivityRow/AimSensitivitySlider");
		aimDeadzoneSlider = GetNode<HSlider>(Sections + "MouseSettings/AimDeadzoneRow/AimDeadzoneSlider");
		aimPowerCurveSlider = GetNode<HSlider>(Sections + "MouseSettings/AimPowerCurveRow/AimPowerCurveSlider");
		autoCenterSpeedSlider = GetNode<HSlider>(Sections + "MouseSettings/AutoCenterSpeedRow/AutoCenterSpeedSlider");
		aimSensitivityValue = GetNode<Label>(Sections + "MouseSettings/AimSensitivityRow/AimSensitivityValue");
		aimDeadzoneValue = GetNode<Label>(Sections + "MouseSettings/AimDeadzoneRow/AimDeadzoneValue");
		aimPowerCurveValue = GetNode<Label>(Sections + "MouseSettings/AimPowerCurveRow/AimPowerCurveValue");
		autoCenterSpeedValue = GetNode<Label>(Sections + "MouseSettings/AutoCenterSpeedRow/AutoCenterSpeedValue");
		aimWidgetCheck = GetNode<CheckBox>(Sections + "MouseSettings/AimWidgetRow/AimWidgetCheck");
		vectorIndicatorCheck = GetNode<CheckBox>(Sections + "MouseSettings/VectorIndicatorRow/VectorIndicatorCheck");
		flightAssistToggleDropdown = GetNode<OptionButton>(Sections + "FlightSettings/FlightAssistToggleRow/FlightAssistToggleDropdown");
		flightAssistToggleDropdown.AddItem("Full / Off", FlightAssistToggleOff);
		flightAssistToggleDropdown.AddItem("Full / Partial", FlightAssistTogglePartial);
		flightAssistModeDropdown = GetNode<OptionButton>(Sections + "FlightSettings/FlightAssistModeRow/FlightAssistModeDropdown");
		flightAssistModeDropdown.AddItem("Thrust", FlightAssistModeThrust);
		flightAssistModeDropdown.AddItem("Throttle", FlightAssistModeThrottle);

		LoadMouseSettings();
		BindMouseSettingsSignals();
		_menuClick = MenuUtils.AttachButtonSounds(this);
		CreateActionList();
	}

	private void LoadMouseSettings()
	{
		var settings = ConfigFileHandler.Instance.LoadControlSettings();

		aimSensitivitySlider.Value = settings.ContainsKey("aim_sensitivity") ? settings["aim_sensitivity"].AsSingle() : 0.5f;
		aimDeadzoneSlider.Value = settings.ContainsKey("aim_deadzone") ? settings["aim_deadzone"].AsSingle() : 0.05f;
		aimPowerCurveSlider.Value = settings.ContainsKey("aim_power_curve") ? settings["aim_power_curve"].AsSingle() : 2.0f;
		autoCenterSpeedSlider.Value = settings.ContainsKey("auto_center_speed") ? settings["auto_center_speed"].AsSingle() : 8.0f;
		aimWidgetCheck.ButtonPressed = !settings.ContainsKey("show_aim_widget") || settings["show_aim_widget"].AsBool();
		vectorIndicatorCheck.ButtonPressed = settings.ContainsKey("show_vector_indicator") && settings["show_vector_indicator"].AsBool();
		bool partial = settings.ContainsKey("toggle_partial") && settings["toggle_partial"].AsBool();
		flightAssistToggleDropdown.Select(partial ? FlightAssistTogglePartial : FlightAssistToggleOff);
		bool throttleMode = settings.ContainsKey("throttle_mode") && settings["throttle_mode"].AsBool();
		flightAssistModeDropdown.Select(throttleMode ? FlightAssistModeThrottle : FlightAssistModeThrust);

		UpdateMouseSettingsUi();
	}

	private void BindMouseSettingsSignals()
	{
		aimSensitivitySlider.ValueChanged += OnAimSensitivityChanged;
		aimDeadzoneSlider.ValueChanged += OnAimDeadzoneChanged;
		aimPowerCurveSlider.ValueChanged += OnAimPowerCurveChanged;
		autoCenterSpeedSlider.ValueChanged += OnAutoCenterSpeedChanged;
		aimWidgetCheck.Toggled += OnAimWidgetToggled;
		vectorIndicatorCheck.Toggled += OnVectorIndicatorToggled;
		flightAssistToggleDropdown.ItemSelected += OnFlightAssistToggleSelected;
		flightAssistModeDropdown.ItemSelected += OnFlightAssistModeSelected;
	}

	private void OnAimWidgetToggled(bool shown)
	{
		ConfigFileHandler.Instance.SaveControlSettings("show_aim_widget", shown);
	}

	private void OnVectorIndicatorToggled(bool shown)
	{
		ConfigFileHandler.Instance.SaveControlSettings("show_vector_indicator", shown);
	}

	private void OnFlightAssistToggleSelected(long index)
	{
		bool partial = flightAssistToggleDropdown.GetItemId((int)index) == FlightAssistTogglePartial;
		ConfigFileHandler.Instance.SaveControlSettings("toggle_partial", partial);
	}

	private void OnFlightAssistModeSelected(long index)
	{
		bool throttleMode = flightAssistModeDropdown.GetItemId((int)index) == FlightAssistModeThrottle;
		ConfigFileHandler.Instance.SaveControlSettings("throttle_mode", throttleMode);
	}

	private void OnAimSensitivityChanged(double value)
	{
		ConfigFileHandler.Instance.SaveControlSettings("aim_sensitivity", (float)value);
		UpdateMouseSettingsUi();
	}

	private void OnAimDeadzoneChanged(double value)
	{
		ConfigFileHandler.Instance.SaveControlSettings("aim_deadzone", (float)value);
		UpdateMouseSettingsUi();
	}

	private void OnAimPowerCurveChanged(double value)
	{
		ConfigFileHandler.Instance.SaveControlSettings("aim_power_curve", (float)value);
		UpdateMouseSettingsUi();
	}

	private void OnAutoCenterSpeedChanged(double value)
	{
		ConfigFileHandler.Instance.SaveControlSettings("auto_center_speed", (float)value);
		UpdateMouseSettingsUi();
	}

	private void UpdateMouseSettingsUi()
	{
		aimSensitivityValue.Text = aimSensitivitySlider.Value.ToString("0.00");
		aimDeadzoneValue.Text = aimDeadzoneSlider.Value.ToString("0.000");
		aimPowerCurveValue.Text = aimPowerCurveSlider.Value.ToString("0.0");
		autoCenterSpeedValue.Text = autoCenterSpeedSlider.Value.ToString("0.0");
	}

	private void CreateActionList()
	{
		foreach (var child in ActionList.GetChildren())
			child.QueueFree();

		foreach (var kvp in InputActions)
		{
			string action = kvp.Key;
			string label = kvp.Value;

			var button = InputButtonScene.Instantiate<Button>();
			var actionLabel = button.FindChild("LabelAction") as Label;
			var inputLabel = button.FindChild("LabelInput") as Label;

			actionLabel.Text = label;

			var events = InputMap.ActionGetEvents(action);
			inputLabel.Text = events.Count > 0 ? InputLabel(events[0]) : "";

			ActionList.AddChild(button);
			ApplyKeybindRowHoverBehavior(button);
			MenuUtils.WireControls(button, _menuClick);
			button.Pressed += () => _on_input_button_pressed(button, action);
		}
	}

	private void ApplyKeybindRowHoverBehavior(Button button)
	{
		var marginContainer = button.FindChild("MarginContainer") as MarginContainer;
		var actionLabel = button.FindChild("LabelAction") as Label;
		var inputLabel = button.FindChild("LabelInput") as Label;
		if (marginContainer == null)
			return;

		marginContainer.AddThemeConstantOverride("margin_left", KeybindRowBaseLeftMargin);
		SetKeybindRowTextColor(actionLabel, inputLabel, KeybindRowNormalTextColor);

		button.MouseEntered += () =>
		{
			marginContainer.AddThemeConstantOverride("margin_left", KeybindRowHoverLeftMargin);
			SetKeybindRowTextColor(actionLabel, inputLabel, KeybindRowHoverTextColor);
		};

		button.MouseExited += () =>
		{
			marginContainer.AddThemeConstantOverride("margin_left", KeybindRowBaseLeftMargin);
			SetKeybindRowTextColor(actionLabel, inputLabel, KeybindRowNormalTextColor);
		};
	}

	private void SetKeybindRowTextColor(Label actionLabel, Label inputLabel, Color color)
	{
		actionLabel?.AddThemeColorOverride("font_color", color);
		inputLabel?.AddThemeColorOverride("font_color", color);
	}

	private void _on_input_button_pressed(Button button, string action)
	{
		if (!IsRemapping)
		{
			IsRemapping = true;
			ActionToRemap = action;
			RemappingButton = button;

			if (button.FindChild("LabelInput") is Label label)
				label.Text = "Press key to bind...";
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (!IsRemapping)
		{
			if (@event.IsActionPressed("menu"))
			{
				GetViewport().SetInputAsHandled();
				_on_back_btn_pressed();
			}
			return;
		}

		if (@event is InputEventKey || (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed))
		{
			InputMap.ActionEraseEvents(ActionToRemap);
			InputMap.ActionAddEvent(ActionToRemap, @event);
			ConfigFileHandler.Instance.SaveKeybindings(ActionToRemap, @event);
			UpdateActionList(RemappingButton, @event);

			IsRemapping = false;
			ActionToRemap = null;
			RemappingButton = null;
			GetViewport().SetInputAsHandled();
		}
	}

	private void UpdateActionList(Button button, InputEvent @event)
	{
		if (button.FindChild("LabelInput") is Label label)
			label.Text = InputLabel(@event);
	}

	private static string InputLabel(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Keycode == Key.None && key.PhysicalKeycode != Key.None)
			return OS.GetKeycodeString(DisplayServer.KeyboardGetKeycodeFromPhysical(key.PhysicalKeycode));
		return @event.AsText().TrimSuffix(" (Physical)");
	}

	private void _on_back_btn_pressed()
	{
		GetTree().ChangeSceneToFile("res://Scenes/Menu.tscn");
	}

	private void _on_reset_btn_pressed()
	{
		ConfigFileHandler.Instance.ResetControlSettings();
		ConfigFileHandler.Instance.ApplyKeybindings();
		LoadMouseSettings();
		CreateActionList();
	}
}