using Godot;

public partial class ConfigFileHandler
{
	private const int MaxNameLength = 16;

	// The name other players see. It is stored in the config file, and also in Steam's profile if Steam is available.
	public string LoadPlayerName()
	{
		if (config.HasSectionKey("profile", "name"))
		{
			string stored = config.GetValue("profile", "name").AsString();
			if (!string.IsNullOrWhiteSpace(stored)) return stored;
		}

		string steamName = SteamManager.Instance?.PersonaName ?? "";
		SavePlayerName(steamName.Trim().Length > 0 ? steamName : $"Pilot {GD.Randi() % 9000 + 1000}");
		return config.GetValue("profile", "name").AsString();
	}

	public void SavePlayerName(string name)
	{
		name = name.Trim();
		if (name.Length > MaxNameLength) name = name[..MaxNameLength];
		if (name.Length == 0) return;

		SaveKey("profile", "name", name);
	}
}
