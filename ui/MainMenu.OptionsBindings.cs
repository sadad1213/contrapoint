using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Contrapoint.Players;

namespace Contrapoint.UI;

public partial class MainMenu
{
	private static readonly (string Action, string Command)[] BindingActions =
	[
		("Move forward", "+forward"), ("Move backward", "+back"), ("Move left (strafe)", "+moveleft"), ("Move right (strafe)", "+moveright"),
		("Jump", "+jump"), ("Duck", "+duck"), ("Walk", "+speed"), ("Turn left", "+left"), ("Turn right", "+right"),
		("Primary attack", "+attack"), ("Secondary attack", "+attack2"), ("Reload", "+reload"), ("Use", "+use"),
		("Inspect weapon", "+lookatweapon"), ("Drop weapon", "drop"), ("Last weapon used", "lastinv"),
		("Next weapon", "invnext"), ("Previous weapon", "invprev"), ("Primary weapon", "slot1"), ("Secondary weapon", "slot2"),
		("Knife", "slot3"), ("Grenades", "slot4"), ("C4 explosive", "slot5"), ("Buy menu", "buymenu"),
		("Buy equipment", "buyequip"), ("Auto buy", "autobuy"), ("Rebuy", "rebuy"), ("Choose team", "chooseteam"),
		("Display scores", "+showscores"), ("Developer console", "toggleconsole"), ("Take screenshot", "jpeg")
	];
	private void BuildBindingPage(Control page)
	{
		_bindingList = new Tree { Name = "KeyBindingsList", Columns = 2, HideRoot = true,
			ColumnTitlesVisible = true, SelectMode = Tree.SelectModeEnum.Row, AllowReselect = true };
		page.AddChild(_bindingList); Rect(_bindingList, 8, 8, 512, 344);
		_bindingList.SetColumnTitle(0, "Action"); _bindingList.SetColumnTitle(1, "Key / Button");
		_bindingList.SetColumnExpandRatio(0, 3); _bindingList.SetColumnExpandRatio(1, 2);
		_bindingList.AddThemeStyleboxOverride("panel", Bevel(new("00000080"), true));
		_bindingList.AddThemeStyleboxOverride("selected", SetupBox("ff9b0080", "00000000"));
		_bindingList.AddThemeStyleboxOverride("selected_focus", SetupBox("ff9b00", "00000000"));
		_bindingList.AddThemeColorOverride("font_selected_color", Colors.Black);
		_bindingList.ItemActivated += BeginBindingCapture;
		DialogButton(page, "ResetToDefaultButton", OptionText("GameUI_Options_Controls_KeyboardResetToDefault"), 8, 360, 168, () =>
		{
			_bindingDraft.Clear();
			if (ConsoleCommands.TryParse(FileAccess.GetFileAsString("res://cfg/default.cfg"), out var commands))
				foreach (var a in commands)
					if (a.Count == 3 && a[0] == "bind" && ConsoleBindings.Normalize(a[1], out var key)) _bindingDraft[key] = a[2];
			_optionApply.Disabled = false; RefreshBindings();
		});
		DialogButton(page, "EditBindingButton", OptionText("GameUI_Options_Controls_KeyboardEditBinding"), 180, 360, 168, BeginBindingCapture);
		DialogButton(page, "ClearBindingButton", OptionText("GameUI_Options_Controls_KeyboardClearBinding"), 352, 360, 168, () =>
		{
			var command = _bindingList.GetSelected()?.GetMetadata(0).AsString();
			if (command == null) return;
			foreach (var key in _bindingDraft.Where(p => p.Value.Equals(command, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToArray())
				_bindingDraft.Remove(key);
			_optionApply.Disabled = false; RefreshBindings();
		});
	}
	private void RefreshBindings()
	{
		if (_bindingList == null) return;
		var selected = _bindingList.GetSelected()?.GetMetadata(0).AsString();
		_bindingList.Clear();
		var root = _bindingList.CreateItem();
		foreach (var action in BindingActions)
		{
			var row = _bindingList.CreateItem(root);
			row.SetText(0, action.Action); row.SetMetadata(0, action.Command);
			row.SetText(1, string.Join(", ", _bindingDraft.Where(p => p.Value.Equals(action.Command, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key)));
			if (selected == action.Command) row.Select(0);
		}
	}
	private void BeginBindingCapture()
	{
		_captureCommand = _bindingList.GetSelected()?.GetMetadata(0).AsString();
		if (_captureCommand == null) return;
		_capturingBinding = true;
		_optionStatus.Text = "Press a key or mouse button. Esc cancels.";
	}
}
