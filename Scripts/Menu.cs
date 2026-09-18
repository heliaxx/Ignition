using Godot;
using System;

public partial class Menu : Control
{
	[ExportGroup("Node References")]
	[Export] private Button controlsButton;
	[Export] private Button graphicsButton;
	[Export] private Button audioButton;
	[Export] private Button pveChallengesButton;
	[Export] private Button multiplayerButton;
	[Export] private Button exitButton;

	private AudioStreamPlayer hoverSound;
	private AudioStreamPlayer clickSound;
	private MenuStack menuStack;

	public override void _Ready()
	{
		GetTree().Paused = false;
		// Gameplay captures the cursor, and not every way back here releases it first.
		Input.MouseMode = Input.MouseModeEnum.Visible;
		EventBus.ClearAll();

		// Found by name, not by path: the menu gets rearranged in the editor often, and a
		// stale path silently leaves the rest of _Ready unwired.
		controlsButton ??= FindChild("ControlsButton") as Button;
		graphicsButton ??= FindChild("GraphicsButton") as Button;
		audioButton ??= FindChild("AudioButton") as Button;
		exitButton ??= FindChild("ExitButton") as Button;
		pveChallengesButton ??= FindChild("PveChallengesButton") as Button;
		multiplayerButton ??= FindChild("MultiplayerButton") as Button;

		hoverSound = new AudioStreamPlayer();
		clickSound = new AudioStreamPlayer();
		hoverSound.Bus = "SFX";
		clickSound.Bus = "SFX";

		AddChild(hoverSound);
		AddChild(clickSound);

		hoverSound.Stream = (AudioStream)GD.Load("res://Imports/Sounds/hover_menu_sound.mp3");
		clickSound.Stream = (AudioStream)GD.Load("res://Imports/Sounds/click_sound_menu2.mp3");

		var musicManager = GetNode<MusicManager>("/root/MusicManager");
		var music = GD.Load<AudioStream>("res://Imports/Sounds/supernova_loop.mp3");
		if (!musicManager.IsPlaying())
			musicManager.PlayMusic(music);

		menuStack = new MenuStack { Name = "MenuStack" };
		AddChild(menuStack);

		pveChallengesButton.Pressed += OnPveChallengesPressed;
		multiplayerButton.Pressed += OnMultiplayerPressed;
		exitButton.Pressed += OnExitButtonPressed;
		controlsButton.Pressed += OnControlsPressed;
		graphicsButton.Pressed += OnGraphicsPressed;
		audioButton.Pressed += OnAudioPressed;

		// Back from a match: the lobby, or why the session ended without this player leaving it.
		if (NetworkManager.Instance.IsActive || MatchManager.Instance.HasLeaveReason)
			OnMultiplayerPressed();

		pveChallengesButton.MouseEntered += OnButtonHovered;
		multiplayerButton.MouseEntered += OnButtonHovered;
		exitButton.MouseEntered += OnButtonHovered;
		controlsButton.MouseEntered += OnButtonHovered;
		graphicsButton.MouseEntered += OnButtonHovered;
		audioButton.MouseEntered += OnButtonHovered;
	}

	private void OnPveChallengesPressed()
	{
		menuStack.Push(GD.Load<PackedScene>("res://Scenes/PveChallenges.tscn"));
	}

	private void OnMultiplayerPressed()
	{
		menuStack.Push(GD.Load<PackedScene>("res://Scenes/MultiplayerPanel.tscn"));
	}

	private void OnExitButtonPressed()
	{
		GetTree().Quit();
	}

	private void OnControlsPressed()
	{
		GetTree().ChangeSceneToFile("res://Scenes/Controls.tscn");
	}

	private void OnGraphicsPressed()
	{
		GetTree().ChangeSceneToFile("res://Scenes/Graphics.tscn");
	}

	private void OnAudioPressed()
	{
		GetTree().ChangeSceneToFile("res://Scenes/Audio.tscn");
	}

	private void OnButtonHovered()
	{
		clickSound.Play();
	}
}
