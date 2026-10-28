public enum MatchMode { Deathmatch, TeamDeathmatch }

// Teams are represented as integers. 0 for no team, 1 is team one, 2 is team two.
public static class Teams
{
	public const int None = 0;
	public const int One = 1;
	public const int Two = 2;

	public static readonly int[] Both = { One, Two };

	public static int Other(int team) => team == One ? Two : One;

	public static string NameOf(int team) => team switch { One => "TEAM 1", Two => "TEAM 2", _ => "" };
}
