using Godot;

public partial class PauseMenu : ColorRect
{
	[ExportGroup("Node References")]
	[Export] private AnimationPlayer animator;
	[Export] private Button continueButton;
	[Export] private Button quitButton;
	[Export] private Button mainMenuButton;

	private AudioStreamPlayer hoverSound;
	private AudioStreamPlayer clickSound;

	private PlayerShip LocalShip => (GetTree().CurrentScene as BaseLevel)?.LocalShip;

	public override void _Ready()
	{
		animator ??= GetNode<AnimationPlayer>("AnimationPlayer");
		continueButton ??= FindChild("ContinueButton") as Button;
		quitButton ??= FindChild("QuitButton") as Button;
		mainMenuButton ??= FindChild("MainMenuButton") as Button;

		if (continueButton == null || quitButton == null || mainMenuButton == null)
		{
			GD.PrintErr("PauseMenu: one or more buttons not found!");
			return;
		}

		hoverSound = new AudioStreamPlayer();
		hoverSound.Bus = "SFX";
		AddChild(hoverSound);
		hoverSound.Stream = (AudioStream)GD.Load("res://Imports/Sounds/hover_menu_sound.mp3");

		clickSound = new AudioStreamPlayer();
		clickSound.Bus = "SFX";
		AddChild(clickSound);
		clickSound.Stream = (AudioStream)GD.Load("res://Imports/Sounds/click_sound_menu2.mp3");

		continueButton.Pressed += () => Unpause();
		quitButton.Pressed += () => GetTree().Quit();
		mainMenuButton.Pressed += () => OnMainMenuButtonPressed();

		continueButton.MouseEntered += Click;
		quitButton.MouseEntered += Click;
		mainMenuButton.MouseEntered += Click;

		Unpause();
	}

	public void Unpause()
	{
		animator.Play("Unpause");
		GetTree().Paused = false;
		if (LocalShip != null) LocalShip.InputSuspended = false;
		Input.MouseMode = Input.MouseModeEnum.Captured;
		SetProcess(false);
		Visible = false;
	}

	public void Pause()
	{
		animator.Play("Pause");
		// A match goes on for everyone else, so there the menu only takes the controls away.
		if (NetworkManager.Instance.IsActive)
		{
			if (LocalShip != null) LocalShip.InputSuspended = true;
		}
		else
		{
			GetTree().Paused = true;
		}
		Input.MouseMode = Input.MouseModeEnum.Visible;
		SetProcess(true);
		Visible = true;
	}

	private void OnMainMenuButtonPressed()
	{
		GetTree().Paused = false;
		MatchManager.Instance.LeaveMatch();
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("menu"))
		{
			if (Visible)
				Unpause();
			else
				Pause();
		}
	}

	private void Click()
	{
		clickSound.Play();
	}
}
