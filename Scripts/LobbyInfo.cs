using System;
using System.Collections.Generic;
using System.Linq;

// What a lobby tells the people browsing for it: who hosts it, who is in, what the match will
// be and whether it is already running. The host keeps it current as plain strings in the
// Steam lobby data or the EOS lobby attributes, so it can be read before joining.
public record LobbyInfo(string Host, IReadOnlyList<LobbyInfo.Member> Members, int KillLimit,
	int TimeLimitMinutes, bool InMatch)
{
	public enum MemberState { Host, Ready, Waiting }

	public record Member(string Name, MemberState State);
	private const string HostKey = "HOST";
	private const string PlayersKey = "PLAYERS";
	private const string KillsKey = "KILLS";
	private const string MinutesKey = "MINUTES";
	private const string StateKey = "STATE";
	private const string MatchState = "match";

	// The server always holds peer id 1.
	private const int ServerPeerId = 1;

	// The lobby this machine is in, as the session currently knows it.
	public static LobbyInfo Current()
	{
		NetworkManager net = NetworkManager.Instance;
		MatchManager match = MatchManager.Instance;
		var members = net.Peers.OrderBy(id => id)
			.Select(id => new Member(net.NameOf(id),
				id == ServerPeerId ? MemberState.Host
				: match.IsReady(id) ? MemberState.Ready
				: MemberState.Waiting))
			.ToList();
		return new LobbyInfo(net.NameOf(ServerPeerId), members, match.KillLimit, match.TimeLimitMinutes, match.InMatch);
	}

	public Dictionary<string, string> ToData() => new()
	{
		[HostKey] = Clean(Host),
		[PlayersKey] = string.Join("\n", Members.Select(m => $"{Clean(m.Name)}\t{(int)m.State}")),
		[KillsKey] = KillLimit.ToString(),
		[MinutesKey] = TimeLimitMinutes.ToString(),
		[StateKey] = InMatch ? MatchState : "lobby",
	};

	// Anything missing or unreadable comes out empty or zero instead of failing: the lobby may
	// have been opened by an older build.
	public static LobbyInfo FromData(Func<string, string> read)
	{
		var members = new List<Member>();
		foreach (string line in (read(PlayersKey) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
		{
			string[] parts = line.Split('\t');
			MemberState state = parts.Length > 1 && int.TryParse(parts[1], out int s) && Enum.IsDefined(typeof(MemberState), s)
				? (MemberState)s
				: MemberState.Waiting;
			members.Add(new Member(parts[0], state));
		}

		string host = read(HostKey);
		return new LobbyInfo(string.IsNullOrEmpty(host) ? "?" : host, members,
			int.TryParse(read(KillsKey), out int kills) ? kills : 0,
			int.TryParse(read(MinutesKey), out int minutes) ? minutes : 0,
			read(StateKey) == MatchState);
	}

	// Tabs and line breaks separate the members and their states.
	private static string Clean(string name) => name.Replace('\t', ' ').Replace('\n', ' ');
}
