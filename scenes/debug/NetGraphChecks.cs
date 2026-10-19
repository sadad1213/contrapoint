using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Players;
using Contrapoint.UI;

public partial class NetGraphChecks : Node3D
{
	private int _checks, _failures;
	private Player _player = null!;
	private NetGraphPanel Graph => _player.Stats.NetGraph;
	private void Check(bool condition, string message)
	{
		_checks++;
		if (condition) return;
		_failures++;
		GD.PushError(message);
	}
	private void Run(string line) => ConsoleCommands.Execute(line,_player.Movement,_player.Console!.Print);
	private async Task Frames(int count = 3)
	{
		for (var i=0;i<count;i++) await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
	}
	private void KeyEvent(Key key, bool down) => GetViewport().PushInput(new InputEventKey { PhysicalKeycode=key, Pressed=down });
	public override async void _Ready()
	{
		try { await Test(); }
		catch(Exception error) { _failures++; GD.PushError(error.ToString()); }
		GD.Print($"NET_GRAPH_CHECKS checks={_checks} failures={_failures}");
		GetTree().Quit(_failures==0?0:1);
	}
	private async Task Test()
	{
		_player=GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		AddChild(_player);
		_player.SetPhysicsProcess(false);
		await Frames();
		Run("net_graph 0");
		await Frames();
		Check(!Graph.Visible,"net_graph 0 hides the overlay.");
		Run("net_graph 1");
		await Frames(12);
		Check(Graph.Visible && Graph.TextLines.Count==3,"net_graph 1 shows the three Source text rows.");
		Check(Graph.FramesPerSecond>0 && float.IsFinite(Graph.FramesPerSecond),"FPS comes from actual frame durations.");
		Check(Graph.TextLines[0].EndsWith("ping: 0 ms") && Graph.TextLines[1].Contains("0.00 k/s") && Graph.TextLines[1].Contains("lerp:   0.0 ms"),
			"An unconnected game reports zero network traffic/latency/interpolation.");
		if(OS.GetCmdlineUserArgs().Contains("--netgraph-screenshots")) await Capture("netgraph_local_720");
		Check(Mathf.Abs(Graph.TextBounds.End.X-GetWindow().Size.X+5)<1,"Default graph is five screen pixels from the right edge.");
		Check(Graph.GetNode<Control>("Graph").MouseFilter==Control.MouseFilterEnum.Ignore,"Overlay does not intercept mouse or console input.");
		Run("net_graphpos 0");
		await Frames();
		Check(Graph.TextBounds.Position.X==5,"net_graphpos 0 anchors the graph on the left.");
		Run("net_graphpos 2");
		await Frames();
		Check(Mathf.Abs(Graph.TextBounds.Position.X-(int)(GetWindow().Size.X-10-Graph.TextBounds.Size.X)/2)<1,"net_graphpos 2 uses Source's centered position.");
		Run("net_graphpos 100");
		await Frames();
		Check(Graph.TextBounds.Position.X==(int)(100*GetWindow().Size.X/640f),"Custom X coordinates use Source's 640-pixel proportional reference.");
		Run("net_graphpos 1; net_graphproportionalfont 0");
		await Frames();
		Check(Graph.FontPixelSize==10,"Nonproportional DefaultFixedOutline remains 10 pixels tall.");
		var estimatedWidth=Graph.TextBounds.Size.X;
		Run("net_graphproportionalfont 1");
		await Frames();
		Check(Graph.FontPixelSize==(int)(10*GetWindow().Size.Y/480f),"Proportional font follows Source's 480-pixel height reference.");
		Check(Graph.TextBounds.Size.X==estimatedWidth,"Source estimates graph width with the proportional font in both font modes.");
		var originalY=Graph.TextBounds.Position.Y;
		Run("net_graphheight 120");
		await Frames();
		Check(Graph.TextBounds.Position.Y<originalY,"Increasing net_graphheight moves the panel upward.");
		Run("net_graphheight 0");
		await Frames();
		Check(Graph.TextBounds.Size.Y==GetWindow().Size.Y-29-Graph.TextBounds.Position.Y,"Graph height cannot shrink below its text rows.");
		Graph.Data.Snapshot=new(PingMilliseconds:35,IncomingKilobytes:12.34f,OutgoingKilobytes:2.5f,
			IncomingPacketsPerSecond:66,OutgoingPacketsPerSecond:66,InterpolationMilliseconds:15.2f,
			Loss:.02f,Choke:.03f,ServerFramesPerSecond:66,ServerFrameDeviationMilliseconds:.12f,UpdateRate:66,CommandRate:66);
		Graph.Data.RecordIncoming(new(Bytes:210,LatencyMilliseconds:35,Payload:new(LocalPlayer:70,Entities:120)));
		Graph.Data.RecordOutgoing(new(Bytes:42,InterpolationTicks:1,Sent:true));
		Graph.Data.RecordOutgoing(new(Bytes:40,InterpolationTicks:1,Sent:true));
		Run("net_graph 2; net_graphheight 64");
		await Frames();
		Check(Graph.TextLines.Count==3 && Graph.TextLines[0].Contains("ping: 35 ms"),"Mode 2 uses the measured channel snapshot.");
		Check(Graph.TextLines[1].Contains("in : 210") && Graph.TextLines[1].Contains("12.34 k/s") && Graph.TextLines[2].Contains("out:  42"),
			"Packet sizes, throughput and the previous outgoing command follow Source's fields.");
		Run("net_graph 3");
		await Frames();
		Check(Graph.TextLines.Count==4 && Graph.TextLines[3].Contains("loss:  2    choke:  3"),"Mode 3 adds loss/choke in percentages.");
		Run("net_graph 4");
		await Frames();
		Check(Graph.TextLines.Count==5 && Graph.TextLines[4].Contains("66.0") && Graph.TextLines[4].Contains("0.12 msec"),"Mode 4 adds server rate and frame deviation.");
		Run("net_graphtext 0");
		await Frames();
		Check(Graph.Visible && Graph.TextLines.Count==0,"net_graphtext hides text while retaining graph rendering.");
		Run("net_graphtext 1; net_graphmsecs 1; net_scale 0");
		Check(Graph.LatencyRangeMilliseconds==50 && Graph.BytesPerPixel==.1f,"Invalid graph scales are corrected to Source's minimums.");
		Run("net_graphmsecs 400; net_scale 5; net_graph 0; bind F9 +graph; bind F10 +graph");
		KeyEvent(Key.F9,true);
		KeyEvent(Key.F10,true);
		await Frames();
		Check(Graph.EffectiveMode==2 && Graph.Visible,"Holding +graph temporarily opens the detailed graph.");
		KeyEvent(Key.F9,false);
		Check(Graph.EffectiveMode==2,"Releasing one of two +graph keys preserves the other held button.");
		KeyEvent(Key.F10,false);
		await Frames();
		Check(Graph.EffectiveMode==0 && !Graph.Visible,"Releasing the last +graph key returns to net_graph 0.");
		Run("+graph");
		_player.Console!.SetOpen(true);
		Check(!Graph.GraphHeld,"Opening console clears a directly held +graph command.");
		_player.Console.SetOpen(false);
		Run("net_graph 3; net_graphpos 2; net_graphheight 90; net_graphsolid 0; host_writeconfig netgraph_roundtrip");
		Run("net_graph 0; net_graphpos 0; net_graphheight 10; net_graphsolid 1; exec netgraph_roundtrip");
		Check(Graph.Mode==3 && Graph.GraphPosition==2 && Graph.GraphHeight==90 && !Graph.Solid,"netgraph settings survive a cfg roundtrip.");
		Run("net_graph NaN; net_graph 1.5; net_graph -1");
		Check(Graph.Mode==3,"Invalid graph modes cannot corrupt the active setting.");
		Check(ConsoleCommands.GetCompletions("net_graph").Contains("net_graphproportionalfont"),"Netgraph parameters participate in console autocomplete.");
		var history=new NetGraphData();
		for(var i=0;i<1100;i++) history.RecordIncoming(new(Bytes:i));
		Check(history.Incoming(0).Bytes==1099 && history.Incoming(1023).Bytes==76,"Packet history wraps at Source's 1024 samples.");
		if(OS.GetCmdlineUserArgs().Contains("--netgraph-screenshots")) await Screenshots();
		Run("net_graph 0; net_graphpos 1; net_graphheight 64; net_graphproportionalfont 1; net_graphsolid 1; net_graphtext 1; unbind F9; unbind F10");
		_player.QueueFree();
		await Frames();
	}
	private async Task Screenshots()
	{
		GetWindow().Mode=Window.ModeEnum.Windowed;
		GetWindow().Size=new(1280,720);
		Run("net_graphpos 1; net_graphheight 64; net_graphsolid 1");
		for(var i=0;i<512;i++)
		{
			Graph.Data.RecordIncoming(new(Bytes:220+i%17*12,LatencyMilliseconds:35+i%9,
				State:i%71==0?NetGraphPacketState.Dropped:i%23==0?NetGraphPacketState.Choked:NetGraphPacketState.Normal,
				Payload:new(LocalPlayer:45,OtherPlayers:32,Entities:95+i%11*9,Sounds:10,Events:8)));
			Graph.Data.RecordOutgoing(new(Bytes:42,InterpolationTicks:.6f+i%13*.07f,Sent:i%3!=0));
		}
		Graph.Data.Snapshot=new(PingMilliseconds:35,IncomingKilobytes:18.25f,OutgoingKilobytes:2.77f,
			IncomingPacketsPerSecond:66,OutgoingPacketsPerSecond:66,InterpolationMilliseconds:30.3f,
			Loss:.01f,Choke:.02f,ServerFramesPerSecond:66,ServerFrameDeviationMilliseconds:.12f,UpdateRate:66,CommandRate:66);
		for(var mode=1;mode<=4;mode++) { Run("net_graph "+mode); await Capture("netgraph_mode"+mode+"_720"); }
		GetWindow().Size=new(1920,1080);
		Run("net_graph 1");
		await Capture("netgraph_1080");
		Run("net_graphproportionalfont 0");
		await Capture("netgraph_fixed_1080");
		GetWindow().Size=new(640,480);
		Run("net_graphproportionalfont 1; net_graph 4");
		await Capture("netgraph_480");
	}
	private async Task Capture(string name)
	{
		await Frames(15);
		await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
		var path="user://console_test/"+name+".png";
		GetViewport().GetTexture().GetImage().SavePng(path);
		GD.Print("NETGRAPH_PREVIEW "+ProjectSettings.GlobalizePath(path));
	}
}
