using Godot;
using Contrapoint.Players;

namespace Contrapoint.UI;

// CSS show_freezepanel/hide_freezepanel: show the imported panel on entering
// freezecam, then freeze the scene when the camera travel finishes.
public partial class DeathScreen : CanvasLayer
{
	private Player _player = null!;
	private TextureRect _frame = null!;
	public CssFreezePanel Panel { get; private set; } = null!;
	private ImageTexture? _image;
	private bool _capturePending, _showPanel;
	public void Bind(Player player)
	{
		_player = player; Layer = 6;
		_frame = new TextureRect { MouseFilter = Control.MouseFilterEnum.Ignore, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale, Visible = false };
		AddChild(_frame);
		Panel = new CssFreezePanel { Name = "FreezePanelBG", Visible = false };
		AddChild(Panel); Panel.Initialize(); Visible = false;
	}
	public void ShowDeath() { ClearDeath(); Visible = true; }
	public void ShowFreezePanel()
	{
		var killer = _player.Death.Killer;
		if (!GodotObject.IsInstanceValid(killer) || killer == _player) return;
		Panel.SetKiller(killer!); _showPanel = true; Panel.Visible = true;
	}
	public void FreezeView()
	{
		if (_capturePending || !_showPanel) return;
		if (DisplayServer.GetName() == "headless") { Panel.Visible = true; return; }
		_capturePending = true; Panel.Visible = false;
		if (_player.KillFeed != null) _player.KillFeed.CaptureSuppressed = true;
		RenderingServer.FramePostDraw += Capture;
	}
	private void Capture()
	{
		RenderingServer.FramePostDraw -= Capture; _capturePending = false;
		if (!_player.Death.Active || !_showPanel) return;
		using var image = GetViewport().GetTexture().GetImage();
		_image = ImageTexture.CreateFromImage(image); _frame.Texture = _image; _frame.Visible = true; Panel.Visible = true;
		if (_player.KillFeed != null) _player.KillFeed.CaptureSuppressed = false;
	}
	public void ClearDeath() { UnfreezeView(); Visible = false; }
	public void UnfreezeView()
	{
		if (_capturePending) RenderingServer.FramePostDraw -= Capture;
		_capturePending = _showPanel = false;
		if (_player.KillFeed != null) _player.KillFeed.CaptureSuppressed = false;
		if (_frame != null) { _frame.Visible = false; _frame.Texture = null; }
		_image?.Dispose(); _image = null;
		if (Panel != null) Panel.Visible = false;
	}
	public override void _Process(double delta)
	{
		Transform = GetViewport().GetStretchTransform().AffineInverse();
		_frame.Size = GetWindow().Size;
		Panel.Visible = _showPanel && !_capturePending && !SourceDeathRules.FreezeCamDisabled
			&& _player.GetNode<CssHud>("HUD/Status").Enabled
			&& _player.Console?.IsOpen != true && _player.Menu?.IsOpen != true;
	}
	public override void _ExitTree() => ClearDeath();
}
