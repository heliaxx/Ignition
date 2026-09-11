using System.Collections.Generic;
using Godot;

// Stable per-match ids for the ships a match scores. A Node3D reference cannot cross the
// network and does not survive a respawn, so anything naming a participant — kill
// attribution now, the damage and match managers later — uses the id instead.
// Ids are assigned locally; a networked match will take them from the server.
public static class Participants
{
	public const int None = 0;

	private sealed class Entry
	{
		public Node3D Ship;
		public string Name;
	}

	private static readonly Dictionary<Node3D, int> _ids = new();
	private static readonly Dictionary<int, Entry> _byId = new();
	private static int _nextId = 1;

	// Call before MatchStats.Reset() on scene change; both are scoped to one match.
	public static void Reset()
	{
		_ids.Clear();
		_byId.Clear();
		_nextId = 1;
	}

	// Offline and AI ships: the local counter names them and the node supplies the label.
	public static int Register(Node3D ship) => Register(ship, None, null);

	// A networked match knows both up front: the server decides the id, and the player chose
	// the name before any ship existed. Safe to call twice; returns the existing id.
	public static int Register(Node3D ship, int id, string name)
	{
		if (ship == null) return None;
		if (_ids.TryGetValue(ship, out int existing)) return existing;

		if (id == None) id = _nextId++;

		// Two ships under one id would make NodeOf ambiguous and merge their scoreboard
		// rows. The local counter and server-assigned peer ids share a range, so this is
		// reachable rather than theoretical.
		if (_byId.TryGetValue(id, out Entry taken) && taken.Ship != ship)
			GD.PushError($"Participants: id {id} already belongs to {taken.Name}");

		_ids[ship] = id;
		// Name captured now so it outlives the node: a freed ship must still be nameable
		// on the scoreboard.
		_byId[id] = new Entry
		{
			Ship = ship,
			Name = string.IsNullOrWhiteSpace(name) ? NameFor(ship) : name,
		};
		return id;
	}

	// None for anything off the roster: asteroids, level hazards, a ship that never
	// registered.
	public static int IdOf(Node3D ship)
	{
		if (ship == null) return None;
		return _ids.TryGetValue(ship, out int id) ? id : None;
	}

	public static string NameOf(int id) => _byId.TryGetValue(id, out Entry entry) ? entry.Name : "?";

	// The ship carrying this id on this machine, or null once it has been freed.
	public static Node3D NodeOf(int id) =>
		_byId.TryGetValue(id, out Entry entry) && GodotObject.IsInstanceValid(entry.Ship)
			? entry.Ship
			: null;

	private static string NameFor(Node3D ship)
	{
		if (ship is Fighter fighter && !string.IsNullOrEmpty(fighter.DisplayName))
			return fighter.DisplayName;
		return ship.Name.ToString();
	}
}
