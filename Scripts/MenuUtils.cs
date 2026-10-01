using Godot;

// Shared menu behavior helpers so submenus sound and feel like the main menu
// and the in-game pause menu.
public static class MenuUtils
{
	// Creates the shared hover-sound player and wires every menu control under
	// root. Returns the player so dynamically rebuilt controls can be wired
	// again later via WireControls.
	public static AudioStreamPlayer AttachButtonSounds(Node root)
	{
		var click = new AudioStreamPlayer();
		click.Bus = "SFX";
		click.Stream = GD.Load<AudioStream>("res://Imports/Sounds/click_sound_menu2.mp3");
		root.AddChild(click);
		WireControls(root, click);
		return click;
	}

	// Recursively gives Buttons the menu hover sound, and applies the
	// pointing-hand cursor to Buttons and Sliders alike.
	public static void WireControls(Node node, AudioStreamPlayer click)
	{
		if (node is Button button)
		{
			button.MouseEntered += () => click.Play();
			button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
		}
		else if (node is Slider slider)
		{
			slider.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
		}
		foreach (Node child in node.GetChildren())
			WireControls(child, click);
	}

	private const string CellColor = "cell_color";

	public static Label Cell(string text, Color color, bool expand = false)
	{
		var cell = new Label
		{
			Text = text,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			VerticalAlignment = VerticalAlignment.Center,
			SizeFlagsHorizontal = expand ? Control.SizeFlags.ExpandFill : Control.SizeFlags.ShrinkEnd,
			SizeFlagsVertical = Control.SizeFlags.Fill,
			ClipText = expand,
		};
		cell.AddThemeColorOverride("font_color", color);
		cell.SetMeta(CellColor, color);
		return cell;
	}

	// A selectable row of cells, for the menus' two-column lists.
	public static Button ListRow(ButtonGroup group, bool selected, params Label[] cells)
	{
		var row = new Button
		{
			ToggleMode = true,
			ButtonGroup = group,
			ButtonPressed = selected,
			CustomMinimumSize = new Vector2(0, 56),
			FocusMode = Control.FocusModeEnum.None,
		};
		var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		line.AddThemeConstantOverride("separation", 24);
		row.AddChild(line);
		line.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		line.OffsetLeft = 16;
		line.OffsetRight = -16;
		foreach (Label cell in cells)
			line.AddChild(cell);

		row.MouseEntered += () => PaintRow(row, hovered: true);
		row.MouseExited += () => PaintRow(row, hovered: false);
		row.Toggled += _ => PaintRow(row, row.IsHovered());
		PaintRow(row, hovered: false);
		return row;
	}

	public static void PaintRow(Button row, bool hovered = false)
	{
		bool filled = !row.Disabled && (row.ButtonPressed || hovered);
		foreach (Label cell in row.GetChild(0).GetChildren())
			cell.AddThemeColorOverride("font_color", filled ? Colors.Black : cell.GetMeta(CellColor).AsColor());
	}
}
