using Godot;
using System;
using System.Linq;

namespace Contrapoint.Maps;

// Every playable wrapper in scenes/maps is available to the menu, console and LAN.
public static class MapCatalog
{
	public sealed record Entry(string Id, string Title, string ScenePath)
	{
		public string NativeScenePath => $"res://assets/maps/{Id}/{Id}.scn";
		public string ThumbnailPath => $"res://assets/ui/css/{Id}.png";
		public bool IsCombat => Id == "2000" || Id.StartsWith("de_", StringComparison.Ordinal) || Id.StartsWith("cs_", StringComparison.Ordinal);
	}

	public static Entry[] Discover() => DirAccess.GetFilesAt("res://scenes/maps")
		.Select(file => file.EndsWith(".remap", StringComparison.Ordinal) ? file[..^6] : file)
		.Where(file => file.EndsWith(".tscn", StringComparison.Ordinal))
		.Distinct(StringComparer.Ordinal)
		.Select(file =>
		{
			var stem = file[..^5];
			// The first two wrappers predate the Source map naming convention.
			var id = stem switch { "dust2" => "de_dust2", "inferno" => "de_inferno", _ => stem };
			var title = id switch
			{
				"2000" => "$2000$",
				"de_dust2" => "DUST II", "de_inferno" => "INFERNO", "kz_jayverb000" => "JAYVERB · KZ",
				_ => (id.StartsWith("de_", StringComparison.Ordinal) || id.StartsWith("cs_", StringComparison.Ordinal) ? id[3..] : id).Replace('_', ' ').ToUpperInvariant()
			};
			return new Entry(id, title, "res://scenes/maps/" + file);
		})
		.OrderBy(entry => entry.Id switch { "de_dust2" => 0, "de_inferno" => 1, "kz_jayverb000" => 2, _ => 3 })
		.ThenBy(entry => entry.Id, StringComparer.Ordinal)
		.ToArray();

	public static Entry? Find(string name) => Discover().FirstOrDefault(entry =>
		entry.ScenePath.Equals(name, StringComparison.Ordinal) || entry.Id.Equals(name, StringComparison.OrdinalIgnoreCase)
		|| entry.Id.Equals("de_" + name, StringComparison.OrdinalIgnoreCase)
		|| entry.Id == "2000" && name == "$2000$");
}
