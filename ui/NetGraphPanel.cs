using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Contrapoint.UI;

// Port of CNetGraphPanel's layout, text fields, palette and graph geometry:
// https://github.com/nillerusr/source-engine/blob/master/game/client/vgui_netgraphpanel.cpp
// Font: SourceSchemeBase.res / DefaultFixedOutline (Lucida Console, tall 10,
// outline 1). Source proportional coordinates use a 640x480 reference.
public partial class NetGraphPanel : CanvasLayer
{
	private const int InterpolationHeight = 24;
	private static readonly Color TextColor = Rgb(229, 229, 178);
	private static readonly Color[] PayloadColors =
	{
		Rgb(0,0,255), Rgb(0,255,0), Rgb(255,0,0), Rgb(255,255,0), Rgb(0,255,255),
		Rgb(128,128,0), Rgb(0,128,128), Rgb(128,0,0), Rgb(0,128,0), Rgb(0,0,128)
	};
	private static readonly string[] LegendNames =
	{
		"localplayer", "otherplayers", "entities", "sounds", "events", "usermessages",
		"entmessages", "stringcmds", "stringtables", "voice"
	};
	public int Mode { get; set; }
	public int GraphPosition { get; set; } = 1;
	public int GraphHeight { get; set; } = 64;
	public bool ProportionalFont { get; set; } = true;
	public bool ShowText { get; set; } = true;
	public bool ShowLatency { get; set; } = true;
	public bool ShowInterpolation { get; set; } = true;
	public bool Solid { get; set; } = true;
	public float BytesPerPixel { get; set; } = 5;
	public int LatencyRangeMilliseconds { get; set; } = 400;
	public bool GraphHeld { get; set; }
	public int EffectiveMode => Mode != 0 ? Mode : GraphHeld ? 2 : 0;
	public NetGraphData Data { get; } = new();
	public float FramesPerSecond => _frameSeconds > 0 ? 1 / _frameSeconds : 0;
	public Rect2 TextBounds { get; private set; }
	public int FontPixelSize { get; private set; }
	public IReadOnlyList<string> TextLines => _textLines;
	private readonly List<string> _textLines = new();
	private readonly record struct TextRun(float X, float Top, string Text, Color Color, bool Small = false, int Size = 0);
	private readonly List<TextRun> _textRuns = new();
	private int _graphX, _graphY, _graphWidth, _graphHeight;
	private Control _canvas = null!;
	private readonly SystemFont _font = GD.Load<SystemFont>("res://ui/net_graph_font.tres");
	private readonly SystemFont _smallFont = new() { FontNames = new[] { "Verdana", "Tahoma" } };
	private float _frameSeconds;
	private int _lastOutgoingBytes;

	public override void _Ready()
	{
		Layer = 81;
		_canvas = new Control { Name = "Graph", MouseFilter = Control.MouseFilterEnum.Ignore };
		AddChild(_canvas);
		_canvas.Draw += DrawGraph;
	}
	public override void _Process(double delta)
	{
		Visible = EffectiveMode != 0;
		if (!Visible) { _textLines.Clear(); _textRuns.Clear(); return; }
		// Draw at physical screen pixels, independently of canvas_items UI stretch.
		Transform = GetViewport().GetStretchTransform().AffineInverse();
		if (ShowText) _frameSeconds = .9f * _frameSeconds + .1f * (float)delta;
		UpdateDisplay();
		_canvas.QueueRedraw();
	}
	private static Color Rgb(int r, int g, int b, int alpha = 255) => new(r/255f,g/255f,b/255f,alpha/255f);
	private static string Format(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
	private float Width(string text, int size = 0) => _font.GetStringSize(text, fontSize: size == 0 ? FontPixelSize : size).X;
	private void Text(float x, float top, string text, Color? color = null, bool small = false)
		=> _textRuns.Add(new(x,top,text,color ?? TextColor,small));
	private void DrawText(TextRun run)
	{
		Font font = run.Small ? _smallFont : _font;
		var size = run.Size != 0 ? run.Size : run.Small ? LegendFontSize() : FontPixelSize;
		var baseline = new Vector2(Mathf.Floor(run.X), Mathf.Floor(run.Top) + font.GetAscent(size));
		if (!run.Small) _canvas.DrawStringOutline(font, baseline, run.Text, fontSize:size, size:4, modulate:Colors.Black);
		_canvas.DrawString(font, baseline, run.Text, fontSize:size, modulate:run.Color);
	}
	private void Right(float right, float top, string text) => Text(right - Width(text) - 1, top, text);
	private void Pixel(float x, float y, float height, Color color, int width = 1)
	{
		if (height <= 0) return;
		_canvas.DrawRect(new Rect2(new Vector2(x, y), new Vector2(width, height)), color);
	}
	private int LegendFontSize()
	{
		var h=GetWindow().Size.Y;
		return h < 600 ? 10 : h < 768 ? 12 : h < 1024 ? 15 : h < 1200 ? 20 : 10;
	}
	private void DrawGraph()
	{
		if (EffectiveMode == 0) return;
		if (EffectiveMode > 1)
		{
			DrawPackets(_graphX,_graphY,_graphWidth,_graphHeight);
			DrawCommands(_graphX,_graphWidth,GetWindow().Size.Y);
		}
		foreach(var run in _textRuns) DrawText(run);
	}
	private void UpdateDisplay()
	{
		_textLines.Clear();
		_textRuns.Clear();
		var mode = EffectiveMode;
		if (mode == 0) return;
		var screen = (Vector2)GetWindow().Size;
		_smallFont.FontWeight = screen.Y >= 1200 ? 900 : 400;
		FontPixelSize = ProportionalFont ? Math.Max(1, (int)(10 * screen.Y / 480)) : 10;
		var proportionalSize = Math.Max(1, (int)(10 * screen.Y / 480));
		// Source estimates width with the proportional font even in fixed-font mode.
		var width = Math.Min(1024, (int)Width("fps:  435  ping: 533 ms lerp 112.3 ms   0/0", proportionalSize));
		width = Math.Min(width, Math.Max(1, (int)screen.X - 10));
		var x = GraphPosition switch
		{
			0 => 5, 1 => (int)screen.X - 5 - width, 2 => ((int)screen.X - 10 - width) / 2,
			_ => Math.Clamp((int)(GraphPosition * screen.X / 640), 5, Math.Max(5, (int)screen.X - width - 5))
		};
		var y = (int)screen.Y - InterpolationHeight - 5;
		var lineHeight = (int)MathF.Ceiling(_font.GetHeight(FontPixelSize));
		var lines = mode > 3 ? 5 : mode > 2 ? 4 : 3;
		var height = Math.Max(GraphHeight, lines * lineHeight);
		TextBounds = new(new Vector2(x, y-height), new Vector2(width, lines*lineHeight));
		_graphX=x; _graphY=y; _graphWidth=width; _graphHeight=height;
		if (ShowText) DrawTextFields(mode,x,y-height,width,lineHeight);
	}
	private void DrawTextFields(int mode, int x, int top, int width, int lineHeight)
	{
		var data=Data.Snapshot;
		var first=Format($"fps:{(int)FramesPerSecond,4}   ping: {(int)data.PingMilliseconds} ms");
		_textLines.Add(first);
		Text(x,top,first);
		Right(x+width,top,Format($"{data.UpdateRate}/s"));
		var incoming=Format($"in :{Data.Incoming(0).Bytes,4}   {data.IncomingKilobytes:F2} k/s ");
		var interpolation=Format($"lerp: {data.InterpolationMilliseconds,5:F1} ms");
		_textLines.Add(incoming+interpolation);
		Text(x,top+lineHeight,incoming);
		var interpColor=TextColor;
		if(data.InterpolationMilliseconds>1)
		{
			if(data.ServerFramesPerSecond<1000/data.InterpolationMilliseconds) interpColor=Rgb(255,255,31);
			else if(data.UpdateRate>0 && data.InterpolationMilliseconds<2000f/data.UpdateRate) interpColor=Rgb(255,125,31);
		}
		Text(x+Width(incoming),top+lineHeight,interpolation,interpColor);
		Right(x+width,top+lineHeight,Format($"{data.IncomingPacketsPerSecond:F1}/s"));
		var outgoingBytes=Data.Outgoing(1).Bytes;
		if(outgoingBytes!=0) _lastOutgoingBytes=outgoingBytes;
		var outgoing=Format($"out:{_lastOutgoingBytes,4}   {data.OutgoingKilobytes:F2} k/s");
		_textLines.Add(outgoing);
		Text(x,top+lineHeight*2,outgoing);
		Right(x+width,top+lineHeight*2,Format($"{data.OutgoingPacketsPerSecond:F1}/s"));
		Right(x+width,top+lineHeight*3,Format($"{data.CommandRate}/s"));
		if(mode<=2) return;
		var loss=Format($"loss:{(int)(data.Loss*100),3}    choke: {(int)(data.Choke*100),2} ");
		_textLines.Add(loss);
		Text(x,top+lineHeight*3,loss);
		if(mode>3)
		{
			var server=Format($"sv  : {data.ServerFramesPerSecond,5:F1}   var: {data.ServerFrameDeviationMilliseconds,4:F2} msec");
			_textLines.Add(server);
			Text(x,top+lineHeight*4,server,data.ServerFramesPerSecond<10?Rgb(255,31,31):data.ServerFramesPerSecond<20?Rgb(255,255,0):TextColor);
		}
		var legendHeight=(int)MathF.Ceiling(_smallFont.GetHeight(LegendFontSize()));
		var legendWidth=(int)_smallFont.GetStringSize("otherplayersWWW",fontSize:LegendFontSize()).X;
		var legendX=x-legendWidth<0 ? x+width+5 : x-legendWidth;
		for(var i=0;i<LegendNames.Length;i++) Text(legendX,top-legendHeight-5-i*legendHeight,LegendNames[i],PayloadColors[i],true);
	}
	private void DrawPackets(int x, int y, int width, int height)
	{
		var maxBytes=0;
		var lastLatencyHeight=0;
		var pingHeight=height-InterpolationHeight-2;
		var scale=Math.Max(.00001f,BytesPerPixel);
		for(var age=0;age<width;age++)
		{
			var packet=Data.Incoming(age);
			maxBytes=Math.Max(maxBytes,packet.Bytes);
			var column=x+width-age-1;
			var normal=packet.State is NetGraphPacketState.Normal or NetGraphPacketState.Choked;
			var h=normal ? Math.Clamp((int)((ShowLatency?packet.LatencyMilliseconds:0)*pingHeight/Math.Max(50,LatencyRangeMilliseconds)),0,pingHeight) : lastLatencyHeight;
			if(normal) lastLatencyHeight=h;
			var color=packet.State switch
			{
				NetGraphPacketState.Dropped => Rgb(255,0,0), NetGraphPacketState.Invalid => Rgb(0,0,255),
				NetGraphPacketState.Skipped => Rgb(240,127,63), NetGraphPacketState.Choked => Rgb(225,225,0), _ => Rgb(63,255,63,232)
			};
			if(normal) Pixel(column,y-h,packet.State==NetGraphPacketState.Choked?2:1,color);
			else for(var j=0;j<h;j++) { var faded=color; faded.A=Mathf.Lerp(color.A,31/255f,j/(float)Math.Max(1,h)); Pixel(column,y-h+j,1,faded); }
			Pixel(column,y,1,Rgb(0,255,0,160));
			Pixel(column,y-height-1,1,Colors.White);
			if(!normal) continue;
			var payloadY=y-height-2;
			for(var category=0;category<PayloadColors.Length;category++)
			{
				var segment=Math.Max(0,(int)(packet.Payload[category]/scale));
				payloadY-=segment;
				if(payloadY<2) break;
				Pixel(column,payloadY,segment,PayloadColors[category]);
			}
			var totalY=y-height-1-(int)(packet.Bytes/scale);
			if(totalY<2) continue;
			Pixel(column,totalY,1,Rgb(240,240,240,128));
			if(Data.Snapshot.LargePacketThreshold>0 && packet.Bytes>Math.Max(300,Data.Snapshot.LargePacketThreshold))
				DrawText(new(column-Width(packet.Bytes.ToString(),10)/2,Math.Max(0,totalY-11),packet.Bytes.ToString(),Colors.White,Size:10));
		}
		var step=Math.Max(1,(int)(10/scale));
		var baseY=y-height-1;
		for(var hatch=baseY;hatch>0 && (baseY-hatch)*scale<maxBytes+50;hatch-=step)
		{
			if((int)((baseY-hatch)*scale)%50==0) Pixel(x,hatch,1,Rgb(0,200,0),4);
			else if(step>5) Pixel(x,hatch,1,Rgb(63,63,0,200),4);
		}
		var snapshot=Data.Snapshot;
		if(snapshot.IncomingStreamProgress>0) Pixel(x,y-height+29,1,Rgb(0,200,0), (int)(width*Math.Clamp(snapshot.IncomingStreamProgress,0,1)));
		if(snapshot.OutgoingStreamProgress>0) Pixel(x,y-height+44,1,Rgb(0,200,0), (int)(width*Math.Clamp(snapshot.OutgoingStreamProgress,0,1)));
	}
	private void DrawCommands(int x, int width, int screenHeight)
	{
		if(!ShowInterpolation) return;
		for(var age=0;age<width;age++)
		{
			var command=Data.Outgoing(age);
			var column=x+width-age-1;
			var h=Math.Clamp((int)(command.InterpolationTicks/3*InterpolationHeight),0,InterpolationHeight);
			var origin=screenHeight-4;
			if(h>=8)
			{
				for(var j=Solid?0:h-9;j<h-8;j++) Pixel(column,origin-8-j,1,InterpolationColor(j+8));
			}
			else for(var j=0;j<(Solid?8-h:1);j++) Pixel(column,origin-h-j,1,InterpolationColor(j+h));
			Pixel(column,origin-8,1,Colors.White);
			Pixel(column,screenHeight-3,1,command.Sent?Rgb(255,255,0):Rgb(0,0,0,200));
		}
	}
	private static Color InterpolationColor(int i) => i<8
		? Rgb((int)(63*(1-i/8f)),(int)(63*i/8f),(int)(100+155*i/8f))
		: Rgb((int)(255-5*(i-8)/16f),(int)(127*(1-(i-8)/16f)),0);
}
