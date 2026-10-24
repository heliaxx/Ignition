using System;

// Global event bus for publishers and subscribers without direct node references.
public static class EventBus
{
	// Audio settings changed (volume sliders, etc.).
	public static event Action AudioSettingsChanged;

	// Sent when any settings change (graphics, controls, etc.)
	public static event Action SettingsChanged;

	// An enemy was killed; the argument is the kill count delta.
	public static event Action<int> EnemyKilled;

	// The player died.
	public static event Action PlayerDied;

	// A ship was destroyed: the victim's participant id and whoever last damaged it
	// (Participants.None if nobody on the roster did).
	public static event Action<int, int> Killed;

	public static void EmitAudioSettingsChanged() => AudioSettingsChanged?.Invoke();
	public static void EmitSettingsChanged() => SettingsChanged?.Invoke();
	public static void EmitEnemyKilled(int count = 1) => EnemyKilled?.Invoke(count);
	public static void EmitPlayerDied() => PlayerDied?.Invoke();
	public static void EmitKilled(int victim, int killer) => Killed?.Invoke(victim, killer);

	// Clears scene-scoped subscriptions; call on scene change to drop stale
	// handlers.
	public static void ClearAll()
	{
		EnemyKilled = null;
		PlayerDied = null;
		Killed = null;
	}
}
