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
	private HSlider autoCenterSpeedSlider;
	private Label aimSensitivityValue;
	private Label aimDeadzoneValue;
	private Label autoCenterSpeedValue;
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
		{ "light", "Light" },
		{ "camera_switch", "Switch camera" },
		{ "target_cycle", "Select target ahead" },
		{ "target_cycle_all", "Cycle targets" },
		{ "free_look", "Look around" }
	};

	public override void _Ready()
	{
		InputButtonScene ??= GD.Load<PackedScene>("res://Scenes/InputButton.tscn");
		ActionList = GetNode<VBoxContainer>("PanelContainer/MarginContainer/VBoxContainer/ScrollContainer/ActionList");
		aimSensitivitySlider = GetNode<HSlider>("PanelContainer/MarginContainer/VBoxContainer/MouseSettings/AimSensitivityRow/AimSensitivitySlider");
		aimDeadzoneSlider = GetNode<HSlider>("PanelContainer/MarginContainer/VBoxContainer/MouseSettings/AimDeadzoneRow/AimDeadzoneSlider");
		autoCenterSpeedSlider = GetNode<HSlider>("PanelContainer/MarginContainer/VBoxContainer/MouseSettings/AutoCenterSpeedRow/AutoCenterSpeedSlider");
		aimSensitivityValue = GetNode<Label>("PanelContainer/MarginContainer/VBoxContainer/MouseSettings/AimSensitivityRow/AimSensitivityValue");
		aimDeadzoneValue = GetNode<Label>("PanelContainer/MarginContainer/VBoxContainer/MouseSettings/AimDeadzoneRow/AimDeadzoneValue");
		autoCenterSpeedValue = GetNode<Label>("PanelContainer/MarginContainer/VBoxContainer/MouseSettings/AutoCenterSpeedRow/AutoCenterSpeedValue");

		LoadMouseSettings();
		BindMouseSettingsSignals();
		_menuClick = MenuUtils.AttachButtonSounds(this);
		CreateActionList();
	}

	private void LoadMouseSettings()
	{
		var settings = ConfigFileHandler.Instance.LoadControlSettings();

		aimSensitivitySlider.Value = settings.ContainsKey("aim_sensitivity") ? settings["aim_sensitivity"].AsSingle() : 1.2f;
		aimDeadzoneSlider.Value = settings.ContainsKey("aim_deadzone") ? settings["aim_deadzone"].AsSingle() : 0.05f;
		autoCenterSpeedSlider.Value = settings.ContainsKey("auto_center_speed") ? settings["auto_center_speed"].AsSingle() : 8.0f;

		UpdateMouseSettingsUi();
	}

	private void BindMouseSettingsSignals()
	{
		aimSensitivitySlider.ValueChanged += OnAimSensitivityChanged;
		aimDeadzoneSlider.ValueChanged += OnAimDeadzoneChanged;
		autoCenterSpeedSlider.ValueChanged += OnAutoCenterSpeedChanged;
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

	private void OnAutoCenterSpeedChanged(double value)
	{
		ConfigFileHandler.Instance.SaveControlSettings("auto_center_speed", (float)value);
		UpdateMouseSettingsUi();
	}

	private void UpdateMouseSettingsUi()
	{
		aimSensitivityValue.Text = aimSensitivitySlider.Value.ToString("0.00");
		aimDeadzoneValue.Text = aimDeadzoneSlider.Value.ToString("0.000");
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
			if (events.Count > 0)
				inputLabel.Text = events[0].AsText().TrimSuffix(" (Physical)");
			else
				inputLabel.Text = "";

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
			label.Text = @event.AsText().TrimSuffix(" (Physical)");
	}

	private void _on_back_btn_pressed()
	{
		GetTree().ChangeSceneToFile("res://Scenes/Menu.tscn");
	}

	private void _on_apply_btn_pressed()
	{
		ConfigFileHandler.Instance.ResetControlSettings();
		ConfigFileHandler.Instance.ApplyKeybindings();
		LoadMouseSettings();
		CreateActionList();
	}
}