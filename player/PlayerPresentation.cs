using Godot;

namespace Contrapoint.Players;

public partial class Player
{
	[Export] public string AgentId { get; set; } = "phoenix";
	public PlayerBody Body { get; private set; } = null!;
	public ThirdPersonCamera ThirdPerson { get; private set; } = null!;
	public FirstPersonCamera? FirstPerson { get; private set; }
	public PlayerDeathState Death { get; private set; } = null!;
	public Contrapoint.UI.CssoKillFeed? KillFeed { get; private set; }
	public Contrapoint.UI.CssTeamMenu? TeamMenu { get; private set; }
	public Contrapoint.UI.CssBuyMenu? BuyMenu { get; private set; }
	public bool IsThirdPerson => ThirdPerson?.Enabled == true;
	public Camera3D RenderCamera => Death?.Active == true ? Death.Camera : IsThirdPerson ? ThirdPerson.Camera : FirstPerson ?? Camera;
	public PlayerCommand MovementIntent { get; private set; }

	private void InitializePresentation()
	{
		Body = new PlayerBody { Name = "Body" };
		AddChild(Body);
		Body.Bind(this);
		Body.Visible = IsAlive && Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist;
		ThirdPerson = new ThirdPersonCamera { Name = "ThirdPerson" };
		AddChild(ThirdPerson);
		ThirdPerson.Bind(this);
		if (LocalInput)
		{
			FirstPerson = new FirstPersonCamera { Name = "FirstPersonCamera" };
			AddChild(FirstPerson);
			FirstPerson.Bind(this);
		}
		PlayerCameraCommands.Register();
		Death = new PlayerDeathState { Name = "DeathState" }; AddChild(Death); Death.Bind(this);
		if (LocalInput)
		{
			KillFeed = new Contrapoint.UI.CssoKillFeed { Name = "KillFeed" };
			AddChild(KillFeed); KillFeed.Bind(this);
			TeamMenu = new Contrapoint.UI.CssTeamMenu { Name = "TeamMenu" };
			AddChild(TeamMenu); TeamMenu.Bind(this);
			BuyMenu = new Contrapoint.UI.CssBuyMenu { Name = "BuyMenu" };
			AddChild(BuyMenu); BuyMenu.Bind(this);
			var matchHud = new Contrapoint.UI.DeathmatchHud { Name = "DeathmatchHud" };
			AddChild(matchHud); matchHud.Bind(this);
		}
		Respawned += Body.ResetPose;
	}

	public void SetThirdPerson(bool enabled) => ThirdPerson.SetEnabled(enabled);
	public bool SetAgent(string id)
	{
		id = id.ToLowerInvariant();
		if (!Body.SetAgent(id)) return false;
		AgentId = id;
		_team = PlayerBody.AgentTeam(id);
		Body.Visible = IsAlive;
		return true;
	}
}
