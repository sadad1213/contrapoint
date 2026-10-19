using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Contrapoint.Players;
using Contrapoint.Weapons;

namespace Contrapoint.UI;

public static partial class ConsoleCommands
{
	// Command names/semantics follow Source engine keys.cpp, cmd.cpp, in_main.cpp,
	// in_mouse.cpp and movevars_shared.cpp (nillerusr/source-engine mirror).
	private sealed record Variable(float Default, float Min, float Max, bool Integer, bool Archive, bool Cheat, bool ServerOnly,
		Func<SourceMovement, float> Get, Action<SourceMovement, float> Set, string Help);
	private static readonly Dictionary<string, Variable> Variables = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, float> Overrides = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase);
	private static int _executionDepth;
	private static int _commandBudget;
	private static bool _initialized;
	private static bool _loading;
	private static bool _configDirty;
	public static bool IsAlias(string name) => Aliases.ContainsKey(name);
	public static string ConfigDirectory => OS.GetCmdlineUserArgs().Contains("--console-test-profile") ? "user://console_test/cfg" : "user://cfg";
	private static Player Owner(SourceMovement movement) => movement.GetParent<Player>();
	private static Crosshair Sight(SourceMovement movement) => Owner(movement).GetNode<Crosshair>("HUD/Crosshair");
	private static string Number(float value) => value.ToString("G9", CultureInfo.InvariantCulture);
	// UI values should not expose binary float/conversion noise. Keep the full
	// round-trip representation above for config serialization.
	private static string DisplayNumber(float value) => value.ToString("G6", CultureInfo.InvariantCulture);
	public static string GetCompletionText(string completion, SourceMovement? movement)
	{
		if (!TryParse(completion, out var commands) || commands.Count == 0) return completion.TrimStart();
		var last = commands[^1];
		if (last.Count != 1 || !Variables.TryGetValue(last[0], out var variable)) return completion.TrimStart();
		var value = movement != null ? variable.Get(movement)
			: Overrides.TryGetValue(last[0], out var saved) ? saved : variable.Default;
		return completion.TrimStart() + " " + DisplayNumber(value);
	}
	public static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

	public static void RegisterVariable(string name, float initial, float min, float max,
		Func<SourceMovement, float> get, Action<SourceMovement, float> set, string help, bool archive = true, bool integer = false,
		bool serverOnly = false, bool cheat = false)
	{
		Variables[name] = new(initial, min, max, integer, archive, cheat, serverOnly, get, set, help);
		Register(name, (args, movement, print) =>
		{
			var variable = Variables[name];
			if (args.Count == 1) { print(VariableDescription(name, movement), false); return; }
			if (serverOnly && Contrapoint.Network.LanSession.Instance?.IsClient == true)
			{ print("Only the server host can change " + name + ".", true); return; }
			if (cheat && !SourceDamageRules.Cheats) { print(name + " requires sv_cheats 1.", true); return; }
			if (args.Count != 2 || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
				!float.IsFinite(value) || value < min || value > max || integer && value != MathF.Truncate(value))
			{ print($"Usage: {name} <{(integer ? "integer" : "number")} {DisplayNumber(min)}..{DisplayNumber(max)}>", true); return; }
			set(movement, value);
			if (serverOnly) Contrapoint.Network.LanSession.Instance?.BroadcastDamageRules();
			Overrides[name] = value;
			if (archive && !_loading) _configDirty = true;
			if (!_loading) print($"\"{name}\" changed to \"{DisplayNumber(get(movement))}\"", false);
			Changed?.Invoke();
		});
	}
	private static string VariableDescription(string name, SourceMovement movement)
	{
		var v = Variables[name];
		return $"\"{name}\" = \"{DisplayNumber(v.Get(movement))}\" ( def. \"{DisplayNumber(v.Default)}\" )\n{(v.Cheat ? "cheat\n" : "")}{v.Help}";
	}
	private static void RegisterBasics()
	{
		RegisterVariable("cl_yawspeed", 210, -100000, 100000, m => Owner(m).KeyboardYawSpeed,
			(m,v) => Owner(m).KeyboardYawSpeed = v, "Keyboard yaw speed in degrees per second.");
		RegisterVariable("cl_pitchspeed", 225, -100000, 100000, m => Owner(m).KeyboardPitchSpeed,
			(m,v) => Owner(m).KeyboardPitchSpeed = v, "Keyboard pitch speed in degrees per second.");
		RegisterVariable("cl_anglespeedkey", .67f, 0, 100000, m => Owner(m).KeyboardAngleSpeedKey,
			(m,v) => Owner(m).KeyboardAngleSpeedKey = v, "Keyboard turn speed multiplier while holding +speed.");
		RegisterVariable("sensitivity", 3, .0001f, 10000000, m => Owner(m).MouseSensitivity / Mathf.DegToRad(.022f),
			(m, v) => Owner(m).MouseSensitivity = Mathf.DegToRad(.022f * v), "Mouse sensitivity (Source degrees per mouse count).");
		RegisterVariable("zoom_sensitivity_ratio", 1, 0, 10000000, m => Owner(m).ZoomSensitivityRatio,
			(m, v) => Owner(m).ZoomSensitivityRatio = v, "Additional mouse sensitivity multiplier while scoped; Source FOV scaling still applies.");
		RegisterVariable("m_yaw", .022f, -10, 10, m => Owner(m).MouseYaw, (m,v) => Owner(m).MouseYaw = v, "Horizontal mouse factor.");
		RegisterVariable("m_pitch", .022f, -10, 10, m => Owner(m).MousePitch, (m,v) => Owner(m).MousePitch = v, "Vertical mouse factor; negative inverts mouse Y.");
		RegisterVariable("m_filter", 0, 0, 1, m => Owner(m).MouseFilter ? 1 : 0, (m,v) => Owner(m).MouseFilter = v != 0, "Average mouse movement over two samples.", integer:true);
		RegisterVariable("m_rawinput", 1, 0, 1, m => Owner(m).RawMouseInput ? 1 : 0, (m,v) => Owner(m).RawMouseInput = v != 0, "Use unscaled screen mouse counts.", integer:true);
		RegisterVariable("cl_mouseenable", 1, 0, 1, m => Owner(m).MouseLookEnabled ? 1 : 0, (m,v) => Owner(m).MouseLookEnabled = v != 0, "Enable mouse look.", integer:true);
		RegisterVariable("volume", 1, 0, 1, _ => AudioServer.GetBusVolumeLinear(0), (_,v) => AudioServer.SetBusVolumeLinear(0,v), "Master sound volume (0..1).");
		RegisterVariable(AirAccelerate, 100, 0, float.MaxValue, m => m.AirAcceleration, (m,v) => m.AirAcceleration=v, "Air acceleration.", false);
		RegisterVariable("sv_accelerate", 5, 0, 10000, m => m.GroundAcceleration, (m,v) => m.GroundAcceleration=v, "Ground acceleration.", false);
		RegisterVariable("sv_gravity", 800, 0, 100000, m => m.Gravity, (m,v) => m.Gravity=v, "Gravity in Source units/s squared.", false);
		RegisterVariable("sv_friction", 4, 0, 1000, m => m.Friction, (m,v) => m.Friction=v, "Ground friction.", false);
		RegisterVariable("sv_stopspeed", 75, 0, 10000, m => m.StopSpeed, (m,v) => m.StopSpeed=v, "Minimum speed used by ground friction.", false);
		RegisterVariable("sv_stepsize", 18, 0, 128, m => m.StepHeight, (m,v) => m.StepHeight=v, "Maximum step height in Source units.", false);
		RegisterVariable("sv_noclipspeed", 5, 0, 1000, m => m.NoclipSpeed/100, (m,v) => m.NoclipSpeed=v*100, "Noclip speed multiplier (100 Source units/s).");
		RegisterVariable("sv_maxspeed", 320, 0, 10000, m => m.MaxSpeed, (m,v) => m.MaxSpeed=v, "Maximum player movement speed.", false);
		RegisterVariable("sv_wateraccelerate", 10, 0, 10000, m => m.WaterAcceleration, (m,v) => m.WaterAcceleration=v, "Water acceleration.", false);
		RegisterVariable("sv_waterfriction", 1, 0, 1000, m => m.WaterFriction, (m,v) => m.WaterFriction=v, "Water friction multiplier.", false);
		RegisterVariable("sv_footsteps", 1, 0, 1, m => Owner(m).MovementAudio.FootstepsEnabled?1:0,
			(m,v) => Owner(m).MovementAudio.FootstepsEnabled=v!=0, "Play player footsteps.", false, true);
		RegisterVariable("cl_bob", .002f, 0, .1f, m => Owner(m).ViewmodelBob.Amount*.002f,
			(m,v) => Owner(m).ViewmodelBob.Amount=v/.002f, "Viewmodel bob amplitude.", archive:false, cheat:true);
		RegisterVariable("cl_bob_style", 1, 0, 1, m => Owner(m).ViewmodelBob.UseNewBob ? 1 : 0,
			(m,v) => Owner(m).ViewmodelBob.UseNewBob=v!=0, "CSSO viewmodel bob style: 0 = CS:S, 1 = CS:GO.", integer:true);
		RegisterVariable("cl_bobcycle", .98f, .1f, 2, m => Owner(m).ViewmodelBob.CycleSeconds, (m,v) => Owner(m).ViewmodelBob.CycleSeconds=v, "CS:GO viewmodel bob cycle multiplier.");
		RegisterVariable("cl_bobcycle_old", .8f, .1f, 2, m => Owner(m).ViewmodelBob.OldCycleSeconds, (m,v) => Owner(m).ViewmodelBob.OldCycleSeconds=v, "CS:S viewmodel bob cycle duration.");
		RegisterVariable("cl_bobamt_vert", .25f, 0, 2, m => Owner(m).ViewmodelBob.VerticalAmount,
			(m,v) => Owner(m).ViewmodelBob.VerticalAmount=v, "CS:GO vertical bob amplitude.");
		RegisterVariable("cl_bobamt_lat", .4f, 0, 2, m => Owner(m).ViewmodelBob.LateralAmount,
			(m,v) => Owner(m).ViewmodelBob.LateralAmount=v, "CS:GO lateral bob amplitude.");
		RegisterVariable("cl_bob_lower_amt", 13, 0, 30, m => Owner(m).ViewmodelBob.LowerAmount,
			(m,v) => Owner(m).ViewmodelBob.LowerAmount=v, "CS:GO viewmodel lowering while running.");
		RegisterVariable("cl_viewmodel_shift_left_amt", 1.5f, 0, 2, m => Owner(m).ViewmodelBob.ShiftLeftAmount,
			(m,v) => Owner(m).ViewmodelBob.ShiftLeftAmount=v, "CS:GO viewmodel shift as shooting accuracy improves.");
		RegisterVariable("cl_viewmodel_shift_right_amt", .75f, 0, 2, m => Owner(m).ViewmodelBob.ShiftRightAmount,
			(m,v) => Owner(m).ViewmodelBob.ShiftRightAmount=v, "CS:GO viewmodel shift as shooting accuracy worsens.");
		RegisterVariable("cl_wpn_sway_scale", 1.6f, 0, 10, m => Owner(m).ViewmodelBob.SwayScale,
			(m,v) => Owner(m).ViewmodelBob.SwayScale=v, "CSSO viewmodel sway scale; 0 disables sway.");
		RegisterVariable("cl_wpn_sway_interp", .1f, 0, 1, m => Owner(m).ViewmodelBob.SwayInterpolation,
			(m,v) => Owner(m).ViewmodelBob.SwayInterpolation=v, "CSSO viewmodel sway history delay in seconds; 0 disables sway.");
		RegisterVariable("cl_bobup", .5f, .01f, .99f, m => Owner(m).ViewmodelBob.UpFraction, (m,v) => Owner(m).ViewmodelBob.UpFraction=v, "Fraction of bob cycle spent moving up.", archive:false, cheat:true);
		RegisterVariable("viewmodel_fov", 58, 20, 120, m => Owner(m).GetNode<Camera3D>("WeaponLayer/Container/Viewport/Camera3D").Fov,
			(m,v) => Owner(m).GetNode<Camera3D>("WeaponLayer/Container/Viewport/Camera3D").Fov=v, "Weapon camera field of view.");
		RegisterVariable("r_drawviewmodel", 1, 0, 1, m => Owner(m).GetNode<SubViewportContainer>("WeaponLayer/Container").Visible ? 1 : 0,
			(m,v) => Owner(m).GetNode<SubViewportContainer>("WeaponLayer/Container").Visible=v!=0, "Draw first-person weapon.", archive:false, integer:true, cheat:true);
		RegisterVariable("crosshair", 1, 0, 1, m => Sight(m).Enabled ? 1 : 0, (m,v) => Sight(m).Enabled=v!=0, "Show crosshair.", integer:true);
		RegisterVariable("cl_crosshairsize", 2, 0, 100, m => Sight(m).ArmLength, (m,v) => Sight(m).ArmLength=v, "Crosshair arm length in 480p units.");
		RegisterVariable("cl_crosshairthickness", 1, 0, 100, m => Sight(m).Thickness, (m,v) => Sight(m).Thickness=v, "Crosshair thickness.");
		RegisterVariable("cl_crosshairgap", -3.75f, -100, 100, m => Sight(m).Gap, (m,v) => Sight(m).Gap=v, "Crosshair center gap.");
		RegisterVariable("cl_crosshaircolor", 5, 0, 5, m => Sight(m).ColorPreset, (m,v) => Sight(m).ColorPreset=(int)v, "0 red, 1 green, 2 yellow, 3 blue, 4 cyan, 5 custom RGB.", integer:true);
		RegisterVariable("cl_crosshaircolor_r", 0, 0, 255, m => MathF.Round(Sight(m).CrosshairColor.R*255), (m,v) => { var s=Sight(m); var c=s.CrosshairColor; c.R=v/255; s.CrosshairColor=c; }, "Custom red component.", integer:true);
		RegisterVariable("cl_crosshaircolor_g", 255, 0, 255, m => MathF.Round(Sight(m).CrosshairColor.G*255), (m,v) => { var s=Sight(m); var c=s.CrosshairColor; c.G=v/255; s.CrosshairColor=c; }, "Custom green component.", integer:true);
		RegisterVariable("cl_crosshaircolor_b", 0, 0, 255, m => MathF.Round(Sight(m).CrosshairColor.B*255), (m,v) => { var s=Sight(m); var c=s.CrosshairColor; c.B=v/255; s.CrosshairColor=c; }, "Custom blue component.", integer:true);
		RegisterVariable("cl_crosshairalpha", 255, 0, 255, m => MathF.Round(Sight(m).CrosshairColor.A*255), (m,v) => { var s=Sight(m); var c=s.CrosshairColor; c.A=v/255; s.CrosshairColor=c; }, "Crosshair opacity.", integer:true);
		RegisterVariable("cl_crosshairusealpha", 1, 0, 1, m => Sight(m).UseAlpha?1:0, (m,v) => Sight(m).UseAlpha=v!=0, "Enable crosshair alpha.", integer:true);
		RegisterVariable("cl_crosshairdot", 0, 0, 1, m => Sight(m).Dot?1:0, (m,v) => Sight(m).Dot=v!=0, "Draw center dot.", integer:true);
		RegisterVariable("cl_crosshair_drawoutline", 0, 0, 1, m => Sight(m).Outline?1:0, (m,v) => Sight(m).Outline=v!=0, "Draw black crosshair outline.", integer:true);
		RegisterVariable("fps_max", 300, 0, 1000, _ => Engine.MaxFps, (_,v) => Engine.MaxFps=(int)v, "Maximum rendering FPS; 0 is unlimited.", integer:true);
		RegisterVariable("r_occlusion", 1, 0, 1, m => m.GetTree().Root.UseOcclusionCulling ? 1 : 0,
			(m,v) => m.GetTree().Root.UseOcclusionCulling = v != 0, "Cull map geometry hidden behind opaque walls. Toggle for performance comparison.", integer:true, archive:false);
		RegisterVariable("mat_vsync", 1, 0, 1, _ => DisplayServer.WindowGetVsyncMode()==DisplayServer.VSyncMode.Disabled?0:1,
			(_,v) => { if (DisplayServer.GetName()!="headless") DisplayServer.WindowSetVsyncMode(v==0?DisplayServer.VSyncMode.Disabled:DisplayServer.VSyncMode.Enabled); }, "Vertical synchronization.", integer:true);
		RegisterVariable("cl_showfps", 0, 0, 1, m => Owner(m).Stats.ShowFps?1:0, (m,v) => Owner(m).Stats.ShowFps=v!=0, "Show rendering FPS.", integer:true);
		RegisterVariable("cl_showpos", 0, 0, 1, m => Owner(m).Stats.ShowPosition?1:0, (m,v) => Owner(m).Stats.ShowPosition=v!=0, "Show position, view angles and velocity.", integer:true);
		RegisterNetGraph();

		foreach (var action in new[] { "forward", "back", "moveleft", "moveright", "left", "right", "lookup", "lookdown", "jump", "duck", "speed", "attack", "attack2", "reload", "use", "lookatweapon", "graph" })
		{
			var button = action;
			Register("+"+button, (_,m,_) => Owner(m).Bindings.Button(button,true));
			Register("-"+button, (_,m,_) => Owner(m).Bindings.Button(button,false));
		}
		for (var i=1; i<=5; i++)
		{
			var slot=(WeaponSlot)(i-1);
			Register("slot"+i, (_,m,_) => Owner(m).Inventory.EquipSlot(slot));
		}
		Register("lastinv", (_,m,_) => Owner(m).Inventory.EquipPrevious());
		Register("invnext", (_,m,_) => CycleWeapon(Owner(m),1));
		Register("invprev", (_,m,_) => CycleWeapon(Owner(m),-1));
		Register("use", (a,m,p) =>
		{
			if(a.Count!=2) { p("Usage: use weapon_<name>",true); return; }
			var name=a[1].Replace("weapon_", "", StringComparison.OrdinalIgnoreCase);
			var weapon=Owner(m).Inventory.Weapons.FirstOrDefault(w => w.SceneFilePath.GetFile().Equals(name+"_view.tscn",StringComparison.OrdinalIgnoreCase));
			if(weapon==null) p("Weapon is not in your inventory: "+a[1],true); else Owner(m).Inventory.Equip(weapon);
		});
		Register("kill", (_,m,_) => Owner(m).TakeDamage(Owner(m).Health));
		Register("respawn", (_,m,_) => Owner(m).Respawn(), cheat:true);
		Register("noclip", (_,m,_) => m.ToggleNoclip(), cheat:true);
		Register("toggleconsole", (_,m,_) => Owner(m).Console?.Toggle());
		Register("clear", (_,m,_) => Owner(m).Console?.Clear());
		Register("stopsound", (_,m,_) =>
		{
			foreach(var node in Owner(m).GetTree().Root.FindChildren("*", "", true, false))
			{
				if(node is AudioStreamPlayer sound) sound.Stop();
				else if(node is AudioStreamPlayer3D spatial) spatial.Stop();
				else if(node is AudioStreamPlayer2D flat) flat.Stop();
			}
		}, cheat:true);
		Register("echo", (a,_,p) => p(string.Join(" ",a.Skip(1)),false));
		Register("bind", Bind, () => ConsoleBindings.Keys.Keys);
		Register("bindtoggle", (a,m,p) =>
		{
			if(a.Count!=3 || !Variables.ContainsKey(a[2])) { p("Usage: bindtoggle <key> <cvar>",true); return; }
			Bind(new[]{"bind",a[1],"toggle "+a[2]},m,p);
		});
		Register("unbind", (a,_,p) =>
		{
			if(a.Count!=2 || !ConsoleBindings.Normalize(a[1],out var key)) { p("Usage: unbind <key>",true); return; }
			if (ConsoleBindings.Keys.Remove(key) && !_loading) _configDirty = true;
		}, () => ConsoleBindings.Keys.Keys);
		Register("unbindall", (_,_,_) =>
		{
			if (ConsoleBindings.Keys.Count > 0 && !_loading) _configDirty = true;
			ConsoleBindings.Keys.Clear();
		});
		Register("key_listboundkeys", (_,_,p) => { foreach(var pair in ConsoleBindings.Keys.OrderBy(x=>x.Key)) p($"{Quote(pair.Key)} = {Quote(pair.Value)}",false); });
		Register("key_findbinding", (a,_,p) =>
		{
			if(a.Count!=2) { p("Usage: key_findbinding <command>",true); return; }
			foreach(var pair in ConsoleBindings.Keys.Where(x=>x.Value.Equals(a[1],StringComparison.OrdinalIgnoreCase))) p(pair.Key,false);
		});
		Register("alias", Alias, () => Aliases.Keys);
		Register("exec", Exec, ConfigNames);
		Register("host_writeconfig", WriteConfig, ConfigNames);
		Register("toggle", Toggle, () => Variables.Keys);
		Register("incrementvar", Increment, () => Variables.Keys);
		Register("help", (a,m,p) =>
		{
			if(a.Count==2 && Variables.ContainsKey(a[1])) p(VariableDescription(a[1],m),false);
			else if(a.Count==2 && Aliases.TryGetValue(a[1],out var body)) p($"alias {a[1]} {Quote(body)}",false);
			else if(a.Count==2 && Commands.ContainsKey(a[1])) p(a[1]+" is a console command.",false);
			else p("Use find <text>, cvarlist [text], cmdlist [text], help <name>. Configs: exec <name>, host_writeconfig [name].",false);
		}, () => Commands.Keys.Concat(Aliases.Keys));
		Register("find", (a,m,p) => ListCommands(a,m,p,Commands.Keys.Concat(Aliases.Keys)), () => Commands.Keys);
		Register("cvarlist", (a,m,p) => ListCommands(a,m,p,Variables.Keys));
		Register("cmdlist", (a,m,p) => ListCommands(a,m,p,Commands.Keys.Except(Variables.Keys).Concat(Aliases.Keys)));
		Register("maps", (_,_,p) => p(string.Join("\n",Contrapoint.Maps.MapCatalog.Discover().Select(map => map.Id)),false));
		Register("disconnect", (_,m,_) => { Contrapoint.Network.LanSession.Instance?.Stop(); Owner(m).GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile,"res://scenes/main_menu.tscn"); });
		Register("quit", (_,m,_) => Owner(m).GetTree().Quit());
		Register("exit", (_,m,_) => Owner(m).GetTree().Quit());
	}
	private static void CycleWeapon(Player player, int direction)
	{
		var weapons=player.Inventory.Weapons.Where(w=>w.CanEquip).ToList();
		if(weapons.Count>0) player.Inventory.Equip(weapons[(weapons.IndexOf(player.ActiveWeapon!)+direction+weapons.Count)%weapons.Count]);
	}
	private static void Bind(IReadOnlyList<string> a, SourceMovement _, Action<string,bool> p)
	{
		if(a.Count<2 || !ConsoleBindings.Normalize(a[1],out var key)) { p("Usage: bind <key> [\"command; command\"] (unknown key)",true); return; }
		if(a.Count==2) { p(ConsoleBindings.Keys.TryGetValue(key,out var value)? $"{Quote(key)} = {Quote(value)}":key+" is not bound.",false); return; }
		ConsoleBindings.Keys[key]=string.Join(" ",a.Skip(2));
		if (!_loading) _configDirty = true;
	}
	private static void Alias(IReadOnlyList<string> a, SourceMovement _, Action<string,bool> p)
	{
		if(a.Count==1) { foreach(var pair in Aliases) p($"alias {pair.Key} {Quote(pair.Value)}",false); return; }
		if(Commands.ContainsKey(a[1]) || a[1].Any(c=>char.IsWhiteSpace(c)||c is ';' or '"' or '\\')) { p("Invalid alias name or name already belongs to a command/cvar.",true); return; }
		Aliases[a[1]]=string.Join(" ",a.Skip(2));
		if (!_loading) _configDirty = true;
		Changed?.Invoke();
	}
	private static void ListCommands(IReadOnlyList<string> a, SourceMovement m, Action<string,bool> p, IEnumerable<string> names)
	{
		var filter=string.Join(" ",a.Skip(1));
		foreach(var name in names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x))
			if(name.Contains(filter,StringComparison.OrdinalIgnoreCase) || Variables.TryGetValue(name,out var v) && v.Help.Contains(filter,StringComparison.OrdinalIgnoreCase))
				p(Variables.TryGetValue(name,out var variable)? $"{name} = {DisplayNumber(variable.Get(m))} : {variable.Help}":name,false);
	}
	private static void Toggle(IReadOnlyList<string> a, SourceMovement m, Action<string,bool> p)
	{
		if(a.Count<2 || !Variables.TryGetValue(a[1],out var v)) { p("Usage: toggle <cvar> [value1 value2 ...]",true); return; }
		var values=a.Count==2 ? new[]{"0","1"} : a.Skip(2).ToArray();
		if(values.Any(x=>!float.TryParse(x,NumberStyles.Float,CultureInfo.InvariantCulture,out var f)||!float.IsFinite(f))) { p("Invalid toggle value.",true); return; }
		var index=Array.FindIndex(values,x=>float.Parse(x,CultureInfo.InvariantCulture)==v.Get(m));
		Execute(a[1]+" "+values[(index+1)%values.Length],m,p);
	}
	private static void Increment(IReadOnlyList<string> a, SourceMovement m, Action<string,bool> p)
	{
		if(a.Count!=5 || !Variables.TryGetValue(a[1],out var v) || !float.TryParse(a[2],NumberStyles.Float,CultureInfo.InvariantCulture,out var min) ||
			!float.TryParse(a[3],NumberStyles.Float,CultureInfo.InvariantCulture,out var max) || !float.TryParse(a[4],NumberStyles.Float,CultureInfo.InvariantCulture,out var delta) ||
			!float.IsFinite(min)||!float.IsFinite(max)||!float.IsFinite(delta)||min>max) { p("Usage: incrementvar <cvar> <min> <max> <delta>",true); return; }
		var value=v.Get(m)+delta;
		if(value>max) value=min; else if(value<min) value=max;
		Execute(a[1]+" "+Number(value),m,p);
	}
	private static bool ConfigName(string name, out string path)
	{
		path="";
		if(name.EndsWith(".cfg",StringComparison.OrdinalIgnoreCase)) name=name[..^4];
		if(string.IsNullOrWhiteSpace(name)||name.Any(c=>!char.IsLetterOrDigit(c)&&c!='_'&&c!='-')) return false;
		path=name+".cfg"; return true;
	}
	private static IEnumerable<string> ConfigNames()
	{
		foreach(var directory in new[]{ConfigDirectory,"res://cfg"})
			if(DirAccess.DirExistsAbsolute(directory)) foreach(var file in DirAccess.GetFilesAt(directory)) if(file.EndsWith(".cfg")) yield return file;
	}
	private static void Exec(IReadOnlyList<string> a, SourceMovement m, Action<string,bool> p)
	{
		if(a.Count!=2 || !ConfigName(a[1],out var file)) { p("Usage: exec <config name>",true); return; }
		var path=ConfigDirectory+"/"+file;
		if(!FileAccess.FileExists(path)) path="res://cfg/"+file;
		if(!FileAccess.FileExists(path)) { p("Couldn't exec "+file,true); return; }
		Execute(FileAccess.GetFileAsString(path),m,p);
	}
	private static void WriteConfig(IReadOnlyList<string> a, SourceMovement m, Action<string,bool> p)
	{
		if(a.Count>2 || !ConfigName(a.Count==1?"config":a[1],out var name)) { p("Usage: host_writeconfig [config name]",true); return; }
		var text=new StringBuilder("// Contrapoint console settings\nunbindall\n");
		foreach(var pair in Aliases.OrderBy(x=>x.Key)) text.AppendLine($"alias {Quote(pair.Key)} {Quote(pair.Value)}");
		foreach(var pair in ConsoleBindings.Keys.OrderBy(x=>x.Key)) text.AppendLine($"bind {Quote(pair.Key)} {Quote(pair.Value)}");
		foreach(var pair in Variables.Where(x=>x.Value.Archive).OrderBy(x=>x.Key)) text.AppendLine(pair.Key+" "+Number(pair.Value.Get(m)));
		// Read the live settings before opening the file, which truncates it.
		DirAccess.MakeDirRecursiveAbsolute(ConfigDirectory);
		using var file=FileAccess.Open(ConfigDirectory+"/"+name,FileAccess.ModeFlags.Write);
		if(file==null) { p("Couldn't write config: "+FileAccess.GetOpenError(),true); return; }
		file.StoreString(text.ToString());
		file.Flush();
		if (file.GetError() != Error.Ok) { p("Couldn't write config: " + file.GetError(), true); return; }
		if (name.Equals("config.cfg", StringComparison.OrdinalIgnoreCase)) _configDirty = false;
		p("Wrote "+ConfigDirectory+"/"+name,false);
	}
	public static void Initialize(Player player)
	{
		ConsoleBindings.RemoveDefaultInputEvents();
		if(_initialized)
		{
			foreach(var pair in Overrides.ToArray())
			{
				var variable = Variables[pair.Key];
				if (variable.Cheat && !SourceDamageRules.Cheats || variable.ServerOnly && Contrapoint.Network.LanSession.Instance?.IsClient == true) continue;
				variable.Set(player.Movement,pair.Value);
			}
			return;
		}
		_initialized=true;
		_loading=true;
		try
		{
			Exec(new[]{"exec","default"},player.Movement,player.Console!.Print);
			if(FileAccess.FileExists(ConfigDirectory+"/config.cfg")) Exec(new[]{"exec","config"},player.Movement,player.Console!.Print);
			if(FileAccess.FileExists(ConfigDirectory+"/autoexec.cfg")||FileAccess.FileExists("res://cfg/autoexec.cfg")) Exec(new[]{"exec","autoexec"},player.Movement,player.Console!.Print);
		}
		finally { _loading=false; }
	}
	public static void Save(Player player)
	{
		if(_initialized && player.Console != null && player.Stats != null) WriteConfig(new[]{"host_writeconfig"},player.Movement,(_,error)=>{ if(error) GD.PushWarning("Could not save console config."); });
	}
}
