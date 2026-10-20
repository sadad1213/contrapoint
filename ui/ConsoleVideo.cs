namespace Contrapoint.UI;

public static partial class ConsoleCommands
{
	private static void RegisterVideo()
	{
		foreach (var d in GamePreferences.Definitions)
		{
			var definition = d;
			RegisterVariable(d.Name, d.Default, d.Min, d.Max, _ => GamePreferences.Value(definition.Name, definition.Default),
				(_, value) => GamePreferences.Set(definition.Name, value), d.Help, integer: d.Integer, global: true);
		}
	}
}
