using Godot;

public partial class ConfigFileHandler
{
	private const int MaxNameLength = 16;

	// The name other players see. An explicit choice wins; the Steam build will fill the
	// blank with the persona name, and until then a random pilot number stands in.
	public string LoadPlayerName()
	{
		if (config.HasSectionKey("profile", "name"))
		{
			string stored = config.GetValue("profile", "name").AsString();
			if (!string.IsNullOrWhiteSpace(stored)) return stored;
		}

		string generated = $"Pilot {GD.Randi() % 9000 + 1000}";
		SavePlayerName(generated);
		return generated;
	}

	public void SavePlayerName(string name)
	{
		name = name.Trim();
		if (name.Length > MaxNameLength) name = name[..MaxNameLength];
		if (name.Length == 0) return;

		SaveKey("profile", "name", name);
	}
}
