using Godot;
using System;
using System.Collections.Generic;

public partial class ConfigFileHandler
{
	private const bool  DefaultRelativeMouse   = true;
	private const float DefaultAimSensitivity  = 1.2f;
	private const float DefaultAimDeadzone     = 0.05f;
	private const float DefaultAutoCenterSpeed = 8.0f;

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
		{ "stop",            "X"       },
		{ "camera_switch",   "C"       },
		{ "primary_fire",    "mouse_1" },
		{ "secondary_fire",  "mouse_2" },
		{ "light",           "L"       },
		{ "free_look",       "mouse_3" },
	};

	public void ResetControlSettings()
	{
		config.SetValue("controls", "relative_mouse",    DefaultRelativeMouse);
		config.SetValue("controls", "aim_sensitivity",   DefaultAimSensitivity);
		config.SetValue("controls", "aim_deadzone",      DefaultAimDeadzone);
		config.SetValue("controls", "auto_center_speed", DefaultAutoCenterSpeed);
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
		Ensure("controls", "aim_deadzone",      DefaultAimDeadzone);
		Ensure("controls", "auto_center_speed", DefaultAutoCenterSpeed);
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
					Keycode = OS.FindKeycodeFromString(eventStr)
				};
			}

			keybindings[key] = inputEvent;
		}

		return keybindings;
	}
}
