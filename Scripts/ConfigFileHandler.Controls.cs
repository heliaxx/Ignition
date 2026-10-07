using Godot;
using System;
using System.Collections.Generic;

public partial class ConfigFileHandler
{
	private const bool  DefaultRelativeMouse   = true;
	// About 6 mm of hand travel to full deflection with a 1200 DPI mouse.
	private const float DefaultAimSensitivity  = 0.5f;
	private const float DefaultAimPowerCurve   = 2.0f;
	private const float DefaultAimDeadzone     = 0.05f;
	private const float DefaultAutoCenterSpeed = 8.0f;
	private const bool  DefaultThrottleMode    = false;
	private const bool  DefaultTogglePartial = false;
	private const bool  DefaultShowAimWidget  = true;
	private const bool  DefaultShowVectorIndicator = false;

	private static readonly Dictionary<string, string> DefaultKeybindings = new()
	{
		{ "thrust_forward",  "W"       },
		{ "thrust_backward", "S"       },
		{ "roll_left",       "Q"       },
		{ "roll_right",      "E"       },
		{ "strafe_up",       "space"   },
		{ "strafe_down",     "alt"     },
		{ "strafe_left",     "A"       },
		{ "strafe_right",    "D"       },
		{ "boost",           "tab"     },
		{ "stop",            "Shift"   },
		{ "relative_mouse",  "X"       },
		{ "flight_assist",   "Z"       },
		{ "camera_switch",   "C"       },
		{ "primary_fire",    "mouse_1" },
		{ "secondary_fire",  "mouse_2" },
		{ "light",           "L"       },
		{ "target_cycle",    "T"       },
		{ "target_cycle_all", "F"      },
		{ "free_look",       "mouse_3" },
		{ "scoreboard",      "F1"      },
	};

	public void ResetControlSettings()
	{
		config.SetValue("controls", "relative_mouse",    DefaultRelativeMouse);
		config.SetValue("controls", "aim_sensitivity",   DefaultAimSensitivity);
		config.SetValue("controls", "aim_power_curve",   DefaultAimPowerCurve);
		config.SetValue("controls", "aim_deadzone",      DefaultAimDeadzone);
		config.SetValue("controls", "auto_center_speed", DefaultAutoCenterSpeed);
		config.SetValue("controls", "throttle_mode", DefaultThrottleMode);
		config.SetValue("controls", "show_aim_widget",  DefaultShowAimWidget);
		config.SetValue("controls", "show_vector_indicator", DefaultShowVectorIndicator);
		config.SetValue("controls", "toggle_partial", DefaultTogglePartial);
		foreach (var kvp in DefaultKeybindings)
			config.SetValue("keybinding", kvp.Key, kvp.Value);
		config.Save(SETTINGS_FILE_PATH);
	}

	private void EnsureControlDefaults()
	{
		bool changed = false;
		void Ensure(string section, string key, Variant value)
		{
			if (config.HasSectionKey(section, key)) return;
			config.SetValue(section, key, value);
			changed = true;
		}
		Ensure("controls", "relative_mouse",    DefaultRelativeMouse);
		Ensure("controls", "aim_sensitivity",   DefaultAimSensitivity);
		Ensure("controls", "aim_power_curve",   DefaultAimPowerCurve);
		Ensure("controls", "aim_deadzone",      DefaultAimDeadzone);
		Ensure("controls", "auto_center_speed", DefaultAutoCenterSpeed);
		Ensure("controls", "throttle_mode", DefaultThrottleMode);
		Ensure("controls", "show_aim_widget",  DefaultShowAimWidget);
		Ensure("controls", "show_vector_indicator", DefaultShowVectorIndicator);
		Ensure("controls", "toggle_partial", DefaultTogglePartial);
		foreach (var kvp in DefaultKeybindings)
			Ensure("keybinding", kvp.Key, kvp.Value);
		if (changed) config.Save(SETTINGS_FILE_PATH);
	}

	public void SaveControlSettings(string key, Variant value)
	{
		SaveKey("controls", key, value);
	}

	public Dictionary<string, Variant> LoadControlSettings()
	{
		return LoadSection("controls");
	}

	// The saved settings, not the project's input map, decide what each action is bound to.
	public void ApplyKeybindings()
	{
		foreach (var (action, inputEvent) in LoadKeybindings())
		{
			InputMap.ActionEraseEvents(action);
			InputMap.ActionAddEvent(action, inputEvent);
		}
	}

	public void SaveKeybindings(StringName action, InputEvent inputEvent)
	{
		string eventStr = "";

		if (inputEvent is InputEventKey keyEvent)
			eventStr = OS.GetKeycodeString(keyEvent.PhysicalKeycode);
		else if (inputEvent is InputEventMouseButton mouseEvent)
			eventStr = $"mouse_{(int)mouseEvent.ButtonIndex}";

		SaveKey("keybinding", action, eventStr);
	}

	public Dictionary<string, InputEvent> LoadKeybindings()
	{
		Dictionary<string, InputEvent> keybindings = new();

		if (!config.HasSection("keybinding"))
			return keybindings;

		var keys = config.GetSectionKeys("keybinding");

		foreach (string key in keys)
		{
			string eventStr = config.GetValue("keybinding", key).AsString();
			InputEvent inputEvent;

			if (eventStr.Contains("mouse_"))
			{
				// Saved as the button's number, but a name ("mouse_Middle") parses too.
				if (!Enum.TryParse(eventStr.Split('_')[1], out MouseButton button))
				{
					GD.PushWarning($"Ignoring unreadable keybinding {key}={eventStr}");
					continue;
				}
				inputEvent = new InputEventMouseButton { ButtonIndex = button };
			}
			else
			{
				inputEvent = new InputEventKey
				{
					PhysicalKeycode = OS.FindKeycodeFromString(eventStr)
				};
			}

			keybindings[key] = inputEvent;
		}

		return keybindings;
	}
}
