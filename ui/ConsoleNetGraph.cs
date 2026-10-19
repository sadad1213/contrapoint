namespace Contrapoint.UI;

public static partial class ConsoleCommands
{
	private static void RegisterNetGraph()
	{
		RegisterVariable("net_graph",0,0,64,m=>Owner(m).Stats.NetGraph.Mode,(m,v)=>Owner(m).Stats.NetGraph.Mode=(int)v,
			"Network graph: 0 off, 1 text, 2 payload/latency/interpolation, 3 legend/loss/choke, 4 server timing.",integer:true);
		RegisterVariable("net_graphpos",1,-16384,16384,m=>Owner(m).Stats.NetGraph.GraphPosition,(m,v)=>Owner(m).Stats.NetGraph.GraphPosition=(int)v,
			"Graph position: 0 left, 1 right, 2 center; other values are proportional X coordinates.",integer:true);
		RegisterVariable("net_graphheight",64,-16384,16384,m=>Owner(m).Stats.NetGraph.GraphHeight,(m,v)=>Owner(m).Stats.NetGraph.GraphHeight=(int)v,
			"Height of the netgraph text panel in screen pixels.",integer:true);
		RegisterVariable("net_graphproportionalfont",1,0,1,m=>Owner(m).Stats.NetGraph.ProportionalFont?1:0,(m,v)=>Owner(m).Stats.NetGraph.ProportionalFont=v!=0,
			"Scale the Source netgraph font with screen height.",integer:true);
		RegisterVariable("net_graphtext",1,0,1,m=>Owner(m).Stats.NetGraph.ShowText?1:0,(m,v)=>Owner(m).Stats.NetGraph.ShowText=v!=0,
			"Draw netgraph text fields.",integer:true);
		RegisterVariable("net_graphsolid",1,0,1,m=>Owner(m).Stats.NetGraph.Solid?1:0,(m,v)=>Owner(m).Stats.NetGraph.Solid=v!=0,
			"Draw filled interpolation bars instead of their endpoints.",integer:true);
		RegisterVariable("net_graphshowlatency",1,0,1,m=>Owner(m).Stats.NetGraph.ShowLatency?1:0,(m,v)=>Owner(m).Stats.NetGraph.ShowLatency=v!=0,
			"Draw the ping and packet-loss graph.",integer:true);
		RegisterVariable("net_graphshowinterp",1,0,1,m=>Owner(m).Stats.NetGraph.ShowInterpolation?1:0,(m,v)=>Owner(m).Stats.NetGraph.ShowInterpolation=v!=0,
			"Draw the command interpolation graph.",integer:true);
		RegisterVariable("net_graphmsecs",400,-100000,100000,m=>Owner(m).Stats.NetGraph.LatencyRangeMilliseconds,(m,v)=>Owner(m).Stats.NetGraph.LatencyRangeMilliseconds=System.Math.Max(50,(int)v),
			"Latency graph range in milliseconds (minimum 50).",integer:true);
		RegisterVariable("net_scale",5,-100000,100000,m=>Owner(m).Stats.NetGraph.BytesPerPixel,(m,v)=>Owner(m).Stats.NetGraph.BytesPerPixel=v<=0?.1f:v,
			"Payload graph bytes per pixel; nonpositive values become 0.1.");
	}
}
