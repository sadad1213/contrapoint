///////////////////////////////////////////////////////////
// Tracker scheme resource file
//
// sections:
//		Colors			- all the colors used by the scheme
//		BaseSettings	- contains settings for app to use to draw controls
//		Fonts			- list of all the fonts used by app
//		Borders			- description of all the borders
//
///////////////////////////////////////////////////////////
Scheme
{
	//////////////////////// COLORS ///////////////////////////
	// color details
	// this is a list of all the colors used by the scheme
	Colors
	{
		// base colors
		"White"						"255 255 255 255"
		"WhiteDim"					"255 255 255 120"

		"Red"						"192 28 0 140"
		"Black"						"0 0 0 255"
		"TransparentBlack"			"0 0 0 196"
		"TransparentLightBlack"		"0 0 0 90"
		"RoundWinPanelBackground"	"0 0 0 179"

		"Blank"						"0 0 0 0"
		"ForTesting"				"255 0 0 32"
		"ForTesting_Magenta"		"255 0 255 255"
		"ForTesting_MagentaDim"		"255 0 255 120"

		// These were ripped from SourceScheme
		"SteamLightGreen"			"157 194 80 255"
		"AchievementsLightGrey"		"79 79 79 255"

		"TeamT"						"234 209 138 255"
		"TeamCT"					"181 212 238 255"

		"HUDColor0"					"232 232 232 255"
		"HUDColor1"					"255 255 255 255"
		"HUDColor2"					"150 200 255 255"
		"HUDColor3"					"53 110 255 255"
		"HUDColor4"					"200 100 255 255"
		"HUDColor5"					"255 41 36 255"
		"HUDColor6"					"255 113 36 255"
		"HUDColor7"					"255 247 36 255"
		"HUDColor8"					"62 255 36 255"
		"HUDColor9"					"36 255 144 255"
		"HUDColor10"				"255 121 153 255"
		"HUDColor11"				"213 226 134 255"
	}

	///////////////////// BASE SETTINGS ////////////////////////
	//
	// default settings for all panels
	// controls use these to determine their settings
	BaseSettings
	{
		// vgui_controls color specifications
		Border.Bright					"255 255 255 36"	// the lit side of a control
		Border.Dark						"255 255 255 36"	// the dark/unlit side of a control
		Border.Selection				"Blank"				// the additional border color for displaying the default/selected button
		Border.BuyPreset				"White"

		Button.TextColor				"White"
		Button.BgColor					"36 36 36 255"
		Button.ArmedTextColor			"White"
		Button.ArmedBgColor				"96 96 96 255"
		Button.SelectedTextColor		"White"
		Button.SelectedBgColor			"16 16 16 255"
		Button.DepressedTextColor		"White"
		Button.DepressedBgColor			"0 0 0 255"
		Button.DisabledBgColor			"24 24 24 255"

		CheckButton.TextColor			"White"
		CheckButton.SelectedTextColor	"White"
		CheckButton.BgColor				"TransparentBlack"
		CheckButton.Border1  			"Border.Dark" 		// the left checkbutton border
		CheckButton.Border2  			"Border.Bright"		// the right checkbutton border
		CheckButton.Check				"White"				// color of the check itself

		ComboBoxButton.ArrowColor		"White"
		ComboBoxButton.ArmedArrowColor	"White"
		ComboBoxButton.BgColor			"TransparentBlack"
		ComboBoxButton.DisabledBgColor	"Blank"

		Frame.BgColor					"TransparentBlack"
		Frame.OutOfFocusBgColor			"TransparentBlack"
		Frame.FocusTransitionEffectTime	"0.0"	// time it takes for a window to fade in/out on focus/out of focus
		Frame.TransitionEffectTime		"0.0"	// time it takes for a window to fade in/out on open/close
		Frame.AutoSnapRange				"0"
		FrameGrip.Color1				"Blank"
		FrameGrip.Color2				"Blank"
		FrameTitleButton.FgColor		"Blank"
		FrameTitleButton.BgColor		"Blank"
		FrameTitleButton.DisabledFgColor	"Blank"
		FrameTitleButton.DisabledBgColor	"Blank"
		FrameSystemButton.FgColor		"Blank"
		FrameSystemButton.BgColor		"Blank"
		FrameSystemButton.Icon			""
		FrameSystemButton.DisabledIcon	""
		FrameTitleBar.TextColor			"White"
		FrameTitleBar.BgColor			"Blank"
		FrameTitleBar.DisabledTextColor	"White"
		FrameTitleBar.DisabledBgColor	"Blank"

		GraphPanel.FgColor				"White"
		GraphPanel.BgColor				"TransparentBlack"

		Label.TextDullColor				"White"
		Label.TextColor					"White"
		Label.TextBrightColor			"White"
		Label.SelectedTextColor			"White"
		Label.BgColor					"Blank"
		Label.DisabledFgColor1			"Blank"
		Label.DisabledFgColor2			"255 255 255 128"

		ListPanel.TextColor					"White"
		ListPanel.BgColor					"TransparentBlack"
		ListPanel.SelectedTextColor			"Black"
		ListPanel.SelectedBgColor			"Red"
		ListPanel.SelectedOutOfFocusBgColor	"Red"
		ListPanel.EmptyListInfoTextColor	"White"

		Menu.TextColor					"White"
		Menu.BgColor					"TransparentBlack"
		Menu.ArmedTextColor				"White"
		Menu.ArmedBgColor				"Red"
		Menu.TextInset					"6"

		Chat.TypingText					"White"

		Panel.FgColor					"WhiteDim"
		Panel.BgColor					"blank"

		HTML.BgColor					"Black"

		"BuyPreset.BgColor"				"0 0 0 128"
		"BuyPresetListBox.BgColor"			"0 0 0 128"
		"Popup.BgColor"					"0 0 0 230"

		ProgressBar.FgColor				"White"
		ProgressBar.BgColor				"TransparentBlack"

		PropertySheet.TextColor			"White"
		PropertySheet.SelectedTextColor	"White"
		PropertySheet.TransitionEffectTime	"0.25"	// time to change from one tab to another

		RadioButton.TextColor			"White"
		RadioButton.SelectedTextColor	"White"

		RichText.TextColor				"White"
		RichText.BgColor				"Blank"
		RichText.SelectedTextColor		"White"
		RichText.SelectedBgColor		"Blank"

		ScrollBarButton.FgColor				"White"
		ScrollBarButton.BgColor				"Blank"
		ScrollBarButton.ArmedFgColor		"White"
		ScrollBarButton.ArmedBgColor		"Blank"
		ScrollBarButton.DepressedFgColor	"White"
		ScrollBarButton.DepressedBgColor	"Blank"

		ScrollBarSlider.FgColor				"Blank"		// nob color
		ScrollBarSlider.BgColor				"Blank"		// slider background color

		SectionedListPanel.HeaderTextColor	"White"
		SectionedListPanel.HeaderBgColor	"Blank"
		SectionedListPanel.DividerColor		"Black"
		SectionedListPanel.TextColor		"White"
		SectionedListPanel.BrightTextColor	"White"
		SectionedListPanel.BgColor			"TransparentLightBlack"
		SectionedListPanel.SelectedTextColor			"Black"
		SectionedListPanel.SelectedBgColor				"Red"
		SectionedListPanel.OutOfFocusSelectedTextColor	"Black"
		SectionedListPanel.OutOfFocusSelectedBgColor	"255 255 255 100"
		SectionedListPanel.ArmedTextColor	"White"
		SectionedListPanel.ArmedBgColor		"200 200 200 12"

		Slider.NobColor				"108 108 108 255"
		Slider.TextColor			"127 140 127 255"
		Slider.TrackColor			"31 31 31 255"
		Slider.DisabledTextColor1	"117 117 117 255"
		Slider.DisabledTextColor2	"30 30 30 255"

		TextEntry.TextColor			"White"
		TextEntry.BgColor			"TransparentBlack"
		TextEntry.CursorColor		"White"
		TextEntry.DisabledTextColor	"White"
		TextEntry.DisabledBgColor	"Blank"
		TextEntry.SelectedTextColor	"Black"
		TextEntry.SelectedBgColor	"Red"
		TextEntry.OutOfFocusSelectedBgColor	"Red"
		TextEntry.FocusEdgeColor	"TransparentBlack"

		ToggleButton.SelectedTextColor	"White"

		Tooltip.TextColor			"200 200 200 255"
		Tooltip.BgColor				"15 15 15 252"

		TreeView.BgColor			"TransparentBlack"

		WizardSubPanel.BgColor		"Blank"

		// scheme-specific colors
		"FgColor"		"White"
		"BgColor"		"TransparentBlack"

		"ViewportBG"		"Blank"
		"team0"			"204 204 204 255" // Spectators
		"team1"			"255 64 64 255" // CT's
		"team2"			"153 204 255 255" // T's

		"MapDescriptionText"	"White" // the text used in the map description window
		"CT_Blue"			"153 204 255 255"
		"T_Red"				"255 64 64 255"
		"Hostage_Yellow"	"Panel.FgColor"
		"HudIcon_Green"		"0 160 0 255"
		"HudIcon_Red"		"160 0 0 255"

		// CHudMenu
		"ItemColor"		"255 167 42 200"	// default 255 167 42 255
		"MenuColor"		"233 208 173 255"
		"MenuBoxBg"		"0 0 0 100"

		// weapon selection colors
		"SelectionNumberFg"		"255 220 0 200"
		"SelectionTextFg"		"255 220 0 200"
		"SelectionEmptyBoxBg" 	"0 0 0 80"
		"SelectionBoxBg" 		"0 0 0 80"
		"SelectionSelectedBoxBg" "0 0 0 190"

		// Hint message colors
		"HintMessageFg"			"255 255 255 255"
		"HintMessageBg" 		"0 0 0 60"

		"ProgressBarFg"			"255 30 13 255"

		// Top-left corner of the "Counter-Strike" on the main screen
		"Main.Title1.X"		"32"
		"Main.Title1.Y"		"32"
		"Main.Title1.Color"	"69 81 137 255"

		// Top-left corner of the "SOURCE OFFENSIVE" on the main screen
		"Main.Title2.X"		"32"
		"Main.Title2.Y"		"72"
		"Main.Title2.Color"	"White"

		// Top-left corner of the "BETA" on the main screen
		"Main.Title3.X"		"460"
		"Main.Title3.Y"		"-10"
		"Main.Title3.Color"	"255 255 0 255"

		// Top-left corner of the menu on the main screen
		"Main.Menu.X"		"32"
		"Main.Menu.Y"		"248"

		// Blank space to leave beneath the menu on the main screen
		"Main.BottomBorder"	"32"

		// base resolution used to size panels
		"ProportionalBase.Width"	"1920"
		"ProportionalBase.Height"	"1080"
	}

	//
	//////////////////////// FONTS /////////////////////////////
	//
	// describes all the fonts
	Fonts
	{
		// fonts are used in order that they are listed
		// fonts listed later in the order will only be used if they fulfill a range not already filled
		// if a font fails to load then the subsequent fonts will replace
		"Default"
		{
			"1"
			{
				"name"		"Stratum2 Regular"
				"tall"		"10"
				"weight"	"900"
				"yres"		"480 599"
				"antialias"	"1"
				"custom"	"1"
			}
			"2"
			{
				"name"		"Stratum2 Regular"
				"tall"		"12"
				"weight"	"900"
				"yres"		"600 767"
				"antialias"	"1"
				"custom"	"1"
			}
			"3"
			{
				"name"		"Stratum2 Regular"
				"tall"		"15"
				"weight"	"900"
				"yres"		"768 1023"
				"antialias"	"1"
				"custom"	"1"
			}
			"4"
			{
				"name"		"Stratum2 Regular"
				"tall"		"20"
				"weight"	"900"
				"yres"		"1024 1199"
				"antialias"	"1"
				"custom"	"1"
			}
			"5"
			{
				"name"		"Stratum2 Regular"
				"tall"		"24"
				"weight"	"900"
				"yres"		"1200 6000"
				"antialias"	"1"
			}
			"6"
			{
				"name"		"Stratum2 Regular"
				"tall"		"12"
				"weight"	"900"
				"antialias"	"1"
				"custom"	"1"
			}
			"7"
			{
				"name"		"Arial"
				"tall"		"12"
				"weight"	"800"
				"antialias"	"1"
				"custom"	"1"
			}
		}
		"DefaultBig"
		{
			"1"
			{
				"name"		"Stratum2 Regular"
				"tall"		"12"
				"weight"	"1100"
				"yres"		"480 599"
				"custom"	"1"
			}
			"2"
			{
				"name"		"Stratum2 Regular"
				"tall"		"15"
				"weight"	"1100"
				"yres"		"600 767"
				"custom"	"1"
			}
			"3"
			{
				"name"		"Stratum2 Regular"
				"tall"		"19"
				"weight"	"1100"
				"yres"		"768 1023"
				"antialias"	"1"
				"custom"	"1"
			}
			"4"
			{
				"name"		"Stratum2 Regular"
				"tall"		"26"
				"weight"	"1100"
				"yres"		"1024 1199"
				"antialias"	"1"
				"custom"	"1"
			}
			"5"
			{
				"name"		"Stratum2 Regular"
				"tall"		"30"
				"weight"	"1100"
				"yres"		"1200 6000"
				"antialias"	"1"
				"custom"	"1"
			}
			"6"
			{
				"name"		"Stratum2 Regular"
				"tall"		"18"
				"weight"	"900"
				"custom"	"1"
			}
		}
		"DefaultLarge"
		{
			"1"
			{
				"name"		"Stratum2 Regular"
				"tall"		"14"
				"weight"	"1100"
				"yres"		"480 599"
				"custom"	"1"
			}
			"2"
			{
				"name"		"Stratum2 Regular"
				"tall"		"18"
				"weight"	"1100"
				"yres"		"600 767"
				"custom"	"1"
			}
			"3"
			{
				"name"		"Stratum2 Regular"
				"tall"		"23"
				"weight"	"1100"
				"yres"		"768 1023"
				"antialias"	"1"
				"custom"	"1"
			}
			"4"
			{
				"name"		"Stratum2 Regular"
				"tall"		"31"
				"weight"	"1100"
				"yres"		"1024 1199"
				"antialias"	"1"
				"custom"	"1"
			}
			"5"
			{
				"name"		"Stratum2 Regular"
				"tall"		"36"
				"weight"	"1100"
				"yres"		"1200 6000"
				"antialias"	"1"
				"custom"	"1"
			}
			"6"
			{
				"name"		"Stratum2 Regular"
				"tall"		"22"
				"weight"	"900"
				"custom"	"1"
			}
		}

		"DefaultLarger"
		{
			"1"
			{
				"name"		"Stratum2 Regular"
				"tall"		"19"
				"weight"	"1100"
				"yres"		"480 599"
				"custom"	"1"
			}
			"2"
			{
				"name"		"Stratum2 Regular"
				"tall"		"24"
				"weight"	"1100"
				"yres"		"600 767"
				"custom"	"1"
			}
			"3"
			{
				"name"		"Stratum2 Regular"
				"tall"		"31"
				"weight"	"1100"
				"yres"		"768 1023"
				"antialias"	"1"
				"custom"	"1"
			}
			"4"
			{
				"name"		"Stratum2 Regular"
				"tall"		"41"
				"weight"	"1100"
				"yres"		"1024 1199"
				"antialias"	"1"
				"custom"	"1"
			}
			"5"
			{
				"name"		"Stratum2 Regular"
				"tall"		"48"
				"weight"	"1100"
				"yres"		"1200 6000"
				"antialias"	"1"
				"custom"	"1"
			}
			"6"
			{
				"name"		"Stratum2 Regular"
				"tall"		"22"
				"weight"	"900"
				"custom"	"1"
			}
		}

		"DefaultVeryLarge"
		{
			"1"
			{
				"name"		"Stratum2 Regular"
				"tall"		"22"
				"weight"	"1300"
				"yres"		"480 599"
				"custom"	"1"
			}
			"2"
			{
				"name"		"Stratum2 Regular"
				"tall"		"28"
				"weight"	"1300"
				"yres"		"600 767"
				"custom"	"1"
			}
			"3"
			{
				"name"		"Stratum2 Regular"
				"tall"		"36"
				"weight"	"1300"
				"yres"		"768 1023"
				"antialias"	"1"
				"custom"	"1"
			}
			"4"
			{
				"name"		"Stratum2 Regular"
				"tall"		"48"
				"weight"	"1300"
				"yres"		"1024 1199"
				"antialias"	"1"
				"custom"	"1"
			}
			"5"
			{
				"name"		"Stratum2 Regular"
				"tall"		"56"
				"weight"	"1300"
				"yres"		"1200 6000"
				"antialias"	"1"
				"custom"	"1"
			}
			"6"
			{
				"name"		"Stratum2 Regular"
				"tall"		"28"
				"weight"	"900"
				"custom"	"1"
			}
		}

		"DefaultGigantic"
		{
			"1"
			{
				"name"		"Stratum2 Regular"
				"tall"		"24"
				"weight"	"1300"
				"yres"		"480 599"
				"custom"	"1"
			}
			"2"
			{
				"name"		"Stratum2 Regular"
				"tall"		"34"
				"weight"	"1300"
				"yres"		"600 767"
				"custom"	"1"
			}
			"3"
			{
				"name"		"Stratum2 Regular"
				"tall"		"42"
				"weight"	"1300"
				"yres"		"768 1023"
				"antialias"	"1"
				"custom"	"1"
			}
			"4"
			{
				"name"		"Stratum2 Regular"
				"tall"		"54"
				"weight"	"1300"
				"yres"		"1024 1199"
				"antialias"	"1"
				"custom"	"1"
			}
			"5"
			{
				"name"		"Stratum2 Regular"
				"tall"		"64"
				"weight"	"1300"
				"yres"		"1200 6000"
				"antialias"	"1"
				"custom"	"1"
			}
			"6"
			{
				"name"		"Stratum2 Regular"
				"tall"		"32"
				"weight"	"900"
				"custom"	"1"
			}
		}

		"UiHeadline"
		{
			"1"
			{
				"name"		"Verdana"
				"tall"		"32"
				"weight"	"700"
				"antialias"	"1"
			}
		}
		"DefaultUnderline"
		{
			"1"
			{
				"name"		"Tahoma"
				"tall"		"12"
				"weight"	"500"
				"underline" "1"
			}
			"2"
			{
				"name"		"Arial"
				"tall"		"11"
				"weight"		"800"
			}
		}
		"DefaultSmall"
		{
			"1"
			{
				"name"		"Verdana"
				"tall"		"10"
				"weight"	"0"
				"yres"	"480 599"
				"antialias"	"1"
			}
			"2"
			{
				"name"		"Verdana"
				"tall"		"12"
				"weight"	"0"
				"yres"	"600 767"
				"antialias"	"1"
			}
			"3"
			{
				"name"		"Verdana"
				"tall"		"15"
				"weight"	"0"
				"yres"	"768 1023"
				"antialias"	"1"
			}
			"4"
			{
				"name"		"Verdana"
				"tall"		"20"
				"weight"	"0"
				"yres"	"1024 1199"
				"antialias"	"1"
			}
			"5"
			{
				"name"		"Verdana"
				"tall"		"24"
				"weight"	"0"
				"yres"	"1200 6000"
				"antialias"	"1"
			}
			"6"
			{
				"name"		"Arial"
				"tall"		"12"
				"weight"		"0"
				"antialias"	"1"
			}
		}
		"CSUnderline"
		{
			"1"
			{
				"name"		"Verdana"
				"tall"		"12"
				"weight"	"0"
				"yres"	"480 599"
				"underline" "1"
			}
			"2"
			{
				"name"		"Verdana"
				"tall"		"13"
				"weight"	"0"
				"yres"	"600 767"
				"underline" "1"
			}
			"3"
			{
				"name"		"Verdana"
				"tall"		"14"
				"weight"	"0"
				"yres"	"768 1023"
				"antialias"	"1"
				"underline" "1"
			}
			"4"
			{
				"name"		"Verdana"
				"tall"		"20"
				"weight"	"0"
				"yres"	"1024 1199"
				"antialias"	"1"
				"underline" "1"
			}
			"5"
			{
				"name"		"Verdana"
				"tall"		"24"
				"weight"	"0"
				"yres"	"1200 6000"
				"antialias"	"1"
				"underline" "1"
			}
			"6"
			{
				"name"		"Arial"
				"tall"		"12"
				"weight"		"0"
				"underline" "1"
			}
		}
		"DefaultVerySmall"
		{
			"1"
			{
				"name"		"Verdana"
				"tall"		"10"
				"weight"	"0"
				"yres"	"480 599"
			}
			"2"
			{
				"name"		"Verdana"
				"tall"		"12"
				"weight"	"0"
				"yres"	"600 767"
			}
			"3"
			{
				"name"		"Verdana"
				"tall"		"15"
				"weight"	"0"
				"yres"	"768 1023"
				"antialias"	"1"
			}
			"4"
			{
				"name"		"Verdana"
				"tall"		"20"
				"weight"	"0"
				"yres"	"1024 1199"
				"antialias"	"1"
			}
			"5"
			{
				"name"		"Verdana"
				"tall"		"24"
				"weight"	"0"
				"yres"	"1200 6000"
				"antialias"	"1"
			}
			"6"
			{
				"name"		"Verdana"
				"tall"		"12"
				"weight"		"0"
			}
			"7"
			{
				"name"		"Arial"
				"tall"		"11"
				"weight"		"0"
			}
		}
		// Used by scoreboard and spectator UI for names which don't map in the normal fashion
		"DefaultVerySmallFallBack"
		{
			"1"
			{
				"name"		"Verdana"
				"tall"		"10"
				"weight"	"0"
				"yres"		"480 599"
				"antialias"	"1"
			}
			"2"
			{
				"name"		"Verdana"
				"tall"		"12"
				"weight"	"0"
				"yres"		"600 1199"
				"antialias"	"1"
			}
			"3"
			{
				"name"		"Verdana"
				"tall"		"15"
				"weight"	"0"
				"yres"		"1200 6000"
				"antialias"	"1"
			}
		}
		CenterPrintText
		{
			// note that this scales with the screen resolution
			"1"
			{
				"name"		"Trebuchet MS" [!$OSX]
				"name"		"Helvetica" [$OSX]
				"tall"		"41"
				"weight"	"900"
				"antialias" "1"
				"additive"	"1"
			}
		}
		HudHintText
		{
			"1"
			{
				"name"		"Verdana"
				"tall"		"12"
				"weight"	"700"
				"yres"	"480 599"
			}
			"2"
			{
				"name"		"Verdana"
				"tall"		"13"
				"weight"	"700"
				"yres"	"600 767"
			}
			"3"
			{
				"name"		"Verdana"
				"tall"		"14"
				"weight"	"700"
				"yres"	"768 1023"
			}
			"4"
			{
				"name"		"Verdana"
				"tall"		"20"
				"weight"	"700"
				"yres"	"1024 1199"
			}
			"5"
			{
				"name"		"Verdana"
				"tall"		"24"
				"weight"	"700"
				"yres"	"1200 10000"
			}
		}
		HudNumbersSmall
		{
			"1"
			{
				"name"		"Arial"
				"tall"		"36"
				"weight"	"1000"
				"additive"	"1"
				"antialias" "1"
			}
		}

		HudSelectionNumbers
		{
			"1"
			{
				"name"		"Verdana"
				"tall"		"25"
				"weight"	"700"
				"antialias" "1"
				"additive"	"1"
			}
		}

		HudSelectionText
		{
			"1"
			{
				"name"		"Verdana"
				"tall"		"8"
				"weight"	"700"
				"antialias" "1"
				"yres"	"1 599"
			}
			"2"
			{
				"name"		"Verdana"
				"tall"		"10"
				"weight"	"700"
				"antialias" "1"
				"yres"	"600 767"
			}
			"3"
			{
				"name"		"Verdana"
				"tall"		"12"
				"weight"	"900"
				"antialias" "1"
				"yres"	"768 1023"
			}
			"4"
			{
				"name"		"Verdana"
				"tall"		"16"
				"weight"	"900"
				"antialias" "1"
				"yres"	"1024 1199"
			}
			"5"
			{
				"name"		"Verdana"
				"tall"		"17"
				"weight"	"1000"
				"antialias" "1"
				"yres"	"1200 10000"
			}
		}

		BudgetLabel
		{
			"1"
			{
				"name"		"Courier New"
				"tall"		"14"
				"weight"	"400"
				"outline"	"1"
			}
		}
		DebugOverlay
		{
			"1"	[$WIN32]
			{
				"name"		"Courier New"
				"tall"		"14"
				"weight"	"400"
				"outline"	"1"
			}
			"1"	[$X360]
			{
				"name"		"Tahoma"
				"tall"		"18"
				"weight"	"200"
				"outline"	"1"
			}
		}
		CSType
		{
			"1"
			{
				"name"  "cs" // cs.ttf
				"tall"  "180"
				"weight" "0"
				"additive" "1"
				"antialias" "1"
				"custom" "1" [$OSX]
			}
		}
		CSweapons // temporary, for testing. overlaps with CSType, above
		{
			"1"
			{
				"name"  "Counter-Strike" // Cstrike.ttf
				"tall"  "158"
				"weight" "0"
				"additive" "1"
				"antialias" "1"
				"custom" "1" [$OSX]
			}
		}
		CSweaponsSmall
		{
			"1"
			{
				"name"  "Counter-Strike" // Cstrike.ttf
				"tall"  "135"
				"weight" "0"
				"additive" "1"
				"antialias" "1"
				"custom" "1" [$OSX]
			}
		}
		CSTypeSmall
		{
			"1"
			{
				"name"  "cs" // cs.ttf
				"tall"  "90"
				"weight" "20"
				"additive" "1"
				"antialias" "1"
				"custom" "1" [$OSX]
			}
		}
		CSTypelr
		{
			"1"
			{
				"name"  "cs" // cs.ttf
				"tall"  "68"
				"weight" "0"
				"additive" "1"
				"antialias" "1"
				"custom" "1" [$OSX]
			}
		}
		CSTypeDeath
		{
			"1"
			{
				"name"  "csd" // csd.ttf
				"tall"  "95"
				"weight" "0"
				"additive" "1"
				"antialias" "1"
				"custom" "1" [$OSX]
			}
		}
		Icons
		{
			"1"
			{
				"name"  "Counter-Strike" // Cstrike.ttf
				"tall"  "63"
				"weight" "0"
				"additive" "1"
				"antialias" "1"
				"custom" "1" [$OSX]
			}
		}
		IconsSmall
		{
			"1"
			{
				"name"  "Counter-Strike" // Cstrike.ttf
				"tall"  "45"
				"weight" "0"
				"additive" "0"
				"antialias" "1"
				"custom" "1" [$OSX]
			}
		}
		"ClientTitle1Font"
		{
			"1"
			{
				"name"		"High Speed" // high-speed.ttf
				"tall"		"100"
				"weight"	"0"
				"additive"	"0"
				"antialias"	"1"
				"custom"	"1"
			}
		}
		"ClientTitle2Font"
		{
			"1"
			{
				"name"		"High Speed" // high-speed.ttf
				"tall"		"60"
				"weight"	"0"
				"additive"	"0"
				"antialias"	"1"
				"custom"	"1"
			}
		}
		"BetaFont"
		{
			"1"
			{
				"name"		"Courier New"
				"tall"		"203"
				"weight"	"900"
				"antialias" "1"
				"additive"	"0"
			}
		}
		HudNumbers
		{
			"1"
			{
				"name"  "Counter-Strike" // Cstrike.ttf
				"tall"  "63"
				"weight" "0"
				"additive" "1"
				"antialias" "1"
			}
			"2"
			{
				"name"  "Verdana"
				"tall"  "63"
				"weight" "0"
				"additive" "1"
				"antialias" "1"
			}
		}
		"CloseCaption_Normal"
		{
			"1"
			{
				"name"		"Tahoma"
				"tall"		"36"
				"weight"	"500"
			}
		}
		"CloseCaption_Italic"
		{
			"1"
			{
				"name"		"Tahoma"
				"tall"		"36"
				"weight"	"500"
				"italic"	"1"
			}
		}
		"CloseCaption_Bold"
		{
			"1"
			{
				"name"		"Tahoma"
				"tall"		"36"
				"weight"	"900"
			}
		}
		"CloseCaption_BoldItalic"
		{
			"1"
			{
				"name"		"Tahoma"
				"tall"		"36"
				"weight"	"900"
				"italic"	"1"
			}
		}
		// this is the symbol font
		"Marlett"
		{
			"1"
			{
				"name"		"Marlett"
				"tall"		"25"
				"weight"	"0"
				"symbol"	"1"
			}
		}
		"MenuTitle"
		{
			"1"
			{
				"name"		"Stratum2 Bold"
				"tall"		"41"
				"weight"	"500"
				"antialias"	"1"
			}
		}
		"Trebuchet24"
		{
			"1"
			{
				"name"		"Trebuchet MS"
				"tall"		"54"
				"weight"	"900"
				"antialias" "1"
				"additive"	"1"
			}
		}
		"Trebuchet20"
		{
			"1"
			{
				"name"		"Trebuchet MS"
				"tall"		"45"
				"weight"	"900"
				"antialias" "1"
				"additive"	"1"
			}
		}
		"Trebuchet18"
		{
			"1"
			{
				"name"		"Trebuchet MS"
				"tall"		"41"
				"weight"	"900"
				"antialias" "1"
				"additive"	"1"
			}
		}
		"TargetID"
		{
			"1"
			{
				"name"		"Verdana"
				"tall"		"12"
				"weight"	"700"
				"yres"	"480 599"
				"dropshadow"	"0"
			}
			"2"
			{
				"name"		"Verdana"
				"tall"		"13"
				"weight"	"700"
				"yres"	"600 767"
				"dropshadow"	"0"
			}
			"3"
			{
				"name"		"Verdana"
				"tall"		"14"
				"weight"	"700"
				"yres"	"768 1023"
				"dropshadow"	"0"
			}
			"4"
			{
				"name"		"Verdana"
				"tall"		"20"
				"weight"	"700"
				"yres"	"1024 1199"
				"dropshadow"	"0"
			}
			"5"
			{
				"name"		"Verdana"
				"tall"		"24"
				"weight"	"700"
				"yres"	"1200 10000"
				"dropshadow"	"0"
			}
		}

		//Stolen from SourceScheme to make the fonts a little smaller
		"AchievementTitleFont"
		{
			"1"
			{
				"name"		"Verdana" [!$OSX]
				"name"		"Verdana Bold" [$OSX]
				"tall"		"36"
				"weight"	"1200"
				"antialias" "1"
				"outline" "1"
			}
		}
		"AchievementDescriptionFont"
		{
			"1"
			{
				"name"		"Verdana" [!$OSX]
				"name"		"Verdana Bold" [$OSX]
				"tall"		"12"
				"weight"	"1200"
				"antialias" "1"
				"outline" "1"
				"yres"		"0 480"
			}
			"2"
			{
				"name"		"Verdana" [!$OSX]
				"name"		"Verdana Bold" [$OSX]
				"tall"		"16"
				"weight"	"1200"
				"antialias" "1"
				"outline" "1"
				"yres"	 "481 10000"
			}
		}


		AchievementItemTitle	[$WIN32]
		{
			"1"
			{
				"name"		"Arial" [!$OSX]
				"name"		"Helvetica Bold" [$OSX]
				"weight"		"1200"
				"tall"			"25"
				"antialias"		"1"
			}
		}

		AchievementItemDescription	[$WIN32]
		{
			"1"
			{
				"name"		"Arial" [!$OSX]
				"name"		"Helvetica" [$OSX]
				"weight"		"800"
				"tall"			"20"
				"antialias"		"1"
			}
		}

		AchievementPopupTitle	[$WIN32]
		{
			"1"
			{
				"name"		"Verdana" [!$OSX]
				"name"		"Helvetica Bold" [$OSX]
				"weight"		"1200"
				"tall"			"27"
				"antialias"		"1"
			}
		}

		AchievementPopupDescription	[$WIN32]
		{
			"1"
			{
				"name"		"Verdana" [!$OSX]
				"name"		"Helvetica" [$OSX]
				"weight"		"700"
				"tall"			"20"
				"antialias"		"1"
			}
		}

		"FreezeSmall"
		{
			"1"
			{
				"name"		"Verdana Bold" [!$OSX]
				"name"		"Helvetica" [$OSX]
				"tall"		"20"
				"weight"	"900"
				"antialias"	"1" [!$OSX]
			}
		}

		"FreezeMedium"	// used by the freeze panel
		{
			"1"
			{
				"name"		"Verdana Bold" [!$OSX]
				"name"		"Helvetica" [$OSX]
				"tall"		"32"
				"weight"	"600"
				"antialias"	"1"
			}
		}

		"FreezeLarge"
		{
			"1"
			{
				"name"		"Verdana Bold" [!$OSX]
				"name"		"Helvetica" [$OSX]
				"tall"		"41"
				"weight"	"600"
				"antialias"	"1"
			}
		}

		"WinPanelLarge"
		{
			"1"
			{
				"name"		"Verdana" [!$OSX]
				"name"		"Helvetica" [$OSX]
				"tall"		"32"
				"weight"	"700"
				"antialias"	"1" [!$OSX]
			}
		}

		"WinPanelTiny"
		{
			"1"
			{
				"name"		"Verdana Bold" [!$OSX]
				"name"		"Verdana" [$OSX]
				"tall"		"20"
				"weight"	"0"
				"antialias"	"1" [!$OSX]
			}
		}

		WinPanelClock
		{
			"1"
			{
				"name"		"Counter-Strike" // Cstrike.ttf
				"tall"		"32"
				"weight"	"0"
				"additive"	"1"
				"antialias"	"1"
			}
		}

		"HUDAchievementTiny"
		{
			"1"
			{
				"name"		"Verdana"
				"tall"		"6"
				"weight"	"0"
				"yres"		"480 599"
			}
			"2"
			{
				"name"		"Verdana"
				"tall"		"8"
				"weight"	"0"
				"yres"		"600 767"
			}
			"3"
			{
				"name"		"Verdana"
				"tall"		"10"
				"weight"	"0"
				"yres"		"768 959"
				"antialias"	"1"
			}
			"4"
			{
				"name"		"Verdana"
				"tall"		"13"
				"weight"	"0"
				"yres"		"960 1023"
				"antialias"	"1" [!$OSX]
			}
			"5"
			{
				"name"		"Verdana"
				"tall"		"14"
				"weight"	"0"
				"yres"		"1024 1199"
				"antialias"	"1" [!$OSX]
			}
			"6"
			{
				"name"		"Verdana"
				"tall"		"16"
				"weight"	"0"
				"yres"		"1200 6000"
				"antialias"	"1" [!$OSX]
			}
		}

		"ScoreboardHeader"
		{
			"1"
			{
				"name"		"verdana bold"
				"tall"		"18"
				"weight"	"0"
				"antialias"	"1"
			}
		}

		"ScoreboardTeamName"
		{
			"1"
			{
				"name"		"verdana bold"
				"tall"		"32"
				"weight"	"0"
				"antialias"	"1"
			}
		}

		"ScoreboardScore"
		{
			"1"
			{
				"name"		"verdana bold"
				"tall"		"101"
				"weight"	"0"
				"antialias"	"1"
			}
		}

		"ScoreboardColumns"
		{
			"1"
			{
				"name"		"verdana bold"
				"tall"		"18"
				"weight"	"0"
				"antialias"	"1" [!$OSX]
			}
		}

		"ScoreboardPlayersAlive"
		{
			"1"
			{
				"name"		"verdana bold"
				"tall"		"27"
				"weight"	"0"
				"antialias"	"1"
			}
		}

		"ScoreboardPlayersAliveSuffix"
		{
			"1"
			{
				"name"		"verdana bold"
				"tall"		"23"
				"weight"	"0"
				"antialias"	"1"
			}
		}

		"ScoreboardBody_1"
		{
			"1"
			{
				"name"		"verdana"
				"tall"		"23" [!$OSX]
				"tall"		"25" [$OSX]
				"weight"	"0"
				"antialias"	"1"
			}
		}

		"ScoreboardBody_2"
		{
			"1"
			{
				"name"		"verdana"
				"tall"		"18" [!$OSX]
				"tall"		"20" [$OSX]
				"weight"	"0"
				"antialias"	"1"
			}
		}

		"ScoreboardBody_3"
		{
			"1"
			{
				"name"		"verdana"
				"tall"		"16" [!$OSX]
				"tall"		"18" [$OSX]
				"weight"	"0"
				"antialias"	"1"
			}
		}

		"ScoreboardMVP"
		{
			"1"
			{
				"name"		"verdana"
				"tall"		"23"
				"weight"	"0"
				"antialias"	"1"
			}
		}

		"HUDAmmoClipFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"52"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"HUDAmmoReserveFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"25"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"HUDAmmoKillCountFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"28"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}

		"HUDHealthArmorNumberFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"48"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}

		"HUDAccountFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"37"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}

		"HUDTeamCounterRoundFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"28"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"HUDTeamCounterAliveCountNumberFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"46"
				"weight"		"0"
				"antialias"		"1"
				"dropshadow"	"1"
				"custom"		"1"
			}
		}
		"HUDTeamCounterAliveCountLabelFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"14"
				"weight"		"0"
				"antialias"		"1"
				"dropshadow"	"1"
				"custom"		"1"
			}
		}
		"HUDTeamCounterLargeAvatarScoreFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"24"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"HUDDeathNoticeNameFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"20"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"HUDGrenadeCrosshairFont"
		{
			"1"
			{
				"name"			"Noto Sans"
				"tall"			"20"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"HUDWeaponSelectionSlotFont"
		{
			"1"
			{
				"name"			"Stratum2 Regular"
				"tall"			"18"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
				"dropshadow"	"1"
			}
		}
		"HUDWeaponSelectionNameFont"
		{
			"1"
			{
				"name"			"Stratum2 Regular"
				"tall"			"20"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
				"dropshadow"	"1"
			}
		}
		"HUDInfoTitleFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"38"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
				"dropshadow"	"1"
			}
		}
		"HUDInfoTextFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"22"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
				"dropshadow"	"1"
			}
		}
		"HUDProgressBarActionFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"22"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
				"dropshadow"	"1"
			}
		}
		"HUDProgressBarTimerFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"20"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"HUDDeathmatchBonusTimerFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"60"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"HUDDeathmatchBonusPointsFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"30"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
				"dropshadow"	"1"
			}
		}
		"HUDDeathmatchBonusWeaponFont"
		{
			"1"
			{
				"name"			"Stratum2 Regular"
				"tall"			"18"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
				"dropshadow"	"1"
			}
		}
		"HUDDeathmatchBonusWeaponNameFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"24"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
				"dropshadow"	"1"
			}
		}
		"HUDUniqueAlertsFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"32"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
				"dropshadow"	"1"
			}
		}
		"HUDVoteFont"
		{
			"1"
			{
				"name"			"Stratum2 Regular"
				"tall"			"22"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"HUDVoteCountFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"28"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"HUDTargetIDFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"24"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"HUDWinPanelWinnerFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"30"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"HUDWinPanelMVPFont"
		{
			"1"
			{
				"name"			"Stratum2 Regular"
				"tall"			"20"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"HUDWinPanelFunFactFont"
		{
			"1"
			{
				"name"			"Stratum2 Regular"
				"tall"			"20"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
				"dropshadow"	"1"
			}
		}
		"HUDLocationFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"30"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"HUDFreezePanelKillerNameFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"36"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"HUDFreezePanelKillerDescriptionFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"22"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"HUDFreezePanelDamageFont"
		{
			"1"
			{
				"name"			"Stratum2 Regular"
				"tall"			"22"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"HUDFreezePanelDamageBoldFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"22"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"HUDFreezePanelScreenshotLabelFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"22"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
				"dropshadow"	"1"
			}
		}
		"HUDVoiceStatusNameFont"
		{
			"1"
			{
				"name"			"Stratum2 Medium"
				"tall"			"22"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
				"dropshadow"	"1"
			}
		}
		"HUDVoiceStatusDisabledFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"14"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}

		"ButtonNumberFont"
		{
			"1"
			{
				"name"			"Stratum2 Regular"
				"tall"			"22"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}

		"TeamMenuButtonFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"22"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"TeamMenuBottomButtonFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"29"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"BuyMenuCategoryButtonFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"28"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"BuyMenuItemButtonFont"
		{
			"1"
			{
				"name"			"Stratum2 Regular"
				"tall"			"20"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"BuyMenuItemButtonPriceFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"24"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"BuyMenuMoneyFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"32"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"BuyMenuItemPreviewNameFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"23"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"BuyMenuItemPreviewDescriptionFont"
		{
			"1"
			{
				"name"			"Stratum2 Regular"
				"tall"			"20"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"BuyMenuSpecialMessageFont"
		{
			"1"
			{
				"name"			"Stratum2 Regular"
				"tall"			"20"
				"weight"		"0"
				"antialias"		"1"
				"dropshadow"	"1"
				"custom"		"1"
			}
		}
		"BuyMenuBuyTimeRemainingFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"38"
				"weight"		"0"
				"antialias"		"1"
				"dropshadow"	"1"
				"custom"		"1"
			}
		}

		"ScoreboardUpperHeaderFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"22"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"ScoreboardBottomHeaderFont"
		{
			"1"
			{
				"name"			"Stratum2 Regular"
				"tall"			"18"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"ScoreboardTeamScoreFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"84"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
				"dropshadow"	"1"
			}
		}
		"ScoreboardTeamNameFont"
		{
			"1"
			{
				"name"			"Stratum2 Regular"
				"tall"			"14"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
				"dropshadow"	"1"
			}
		}
		"ScoreboardPlayerListRowFont"
		{
			"1"
			{
				"name"			"Stratum2 Regular"
				"tall"			"18"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"ScoreboardPlayerListHeaderFont"
		{
			"1"
			{
				"name"			"Stratum2 Regular"
				"tall"			"16"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"ScoreboardTipFont"
		{
			"1"
			{
				"name"			"Stratum2 Regular"
				"tall"			"16"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"SpectatorRoundTimerFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"41"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"SpectatorRoundNumberFont"
		{
			"1"
			{
				"name"			"Stratum2 Medium"
				"tall"			"24"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"SpectatorTeamScoreFont"
		{
			"1"
			{
				"name"			"Stratum2 Bold"
				"tall"			"84"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"SpectatorTeamNameFont"
		{
			"1"
			{
				"name"			"Stratum2 Medium"
				"tall"			"46"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
			}
		}
		"SpectatorPlayerNameFont"
		{
			"1"
			{
				"name"			"Stratum2 Medium"
				"tall"			"24"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
				"dropshadow"	"1"
			}
		}
		"SpectatorPlayerStatsHeaderFont"
		{
			"1"
			{
				"name"			"Stratum2 Medium"
				"tall"			"10"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
				"dropshadow"	"1"
			}
		}
		"SpectatorPlayerStatsValueFont"
		{
			"1"
			{
				"name"			"Stratum2 Regular"
				"tall"			"18"
				"weight"		"0"
				"antialias"		"1"
				"custom"		"1"
				"dropshadow"	"1"
			}
		}
	}

	//
	//////////////////// BORDERS //////////////////////////////
	//
	// describes all the border types
	Borders
	{
		BaseBorder
		{
			"inset" "0 0 1 1"
			Left
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 1"
				}
			}

			Right
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "1 0"
				}
			}

			Top
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 0"
				}
			}

			Bottom
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "0 0"
				}
			}
		}

		TitleButtonBorder
		{
			"inset" "0 0 1 1"
			Left
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "0 1"
				}
			}

			Right
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "1 0"
				}
			}

			Top
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "0 0"
				}
			}

			Bottom
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 0"
				}
			}
		}

		TitleButtonDisabledBorder
		{
			"inset" "0 0 1 1"
			Left
			{
				"1"
				{
					"color" "BgColor"
					"offset" "0 1"
				}
			}

			Right
			{
				"1"
				{
					"color" "BgColor"
					"offset" "1 0"
				}
			}
			Top
			{
				"1"
				{
					"color" "BgColor"
					"offset" "0 0"
				}
			}

			Bottom
			{
				"1"
				{
					"color" "BgColor"
					"offset" "0 0"
				}
			}
		}

		TitleButtonDepressedBorder
		{
			"inset" "1 1 1 1"
			Left
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 1"
				}
			}

			Right
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "1 0"
				}
			}

			Top
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 0"
				}
			}

			Bottom
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "0 0"
				}
			}
		}

		ScrollBarButtonBorder
		{
			"inset" "1 0 0 0"
			Left
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "0 1"
				}
			}

			Right
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "1 0"
				}
			}

			Top
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "0 0"
				}
			}

			Bottom
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 0"
				}
			}
		}

		ScrollBarButtonDepressedBorder
		{
			"inset" "2 2 0 0"
			Left
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 1"
				}
			}

			Right
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "1 0"
				}
			}

			Top
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 0"
				}
			}

			Bottom
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "0 0"
				}
			}
		}

		ButtonBorder
		{
			"inset" "0 0 0 0"
			Left
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "0 2"
				}
				"2"
				{
					"color" "Border.Bright"
					"offset" "0 2"
				}
			}

			Right
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 2"
				}
				"2"
				{
					"color" "Border.Dark"
					"offset" "0 2"
				}
			}

			Top
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "2 2"
				}
				"2"
				{
					"color" "Border.Bright"
					"offset" "2 2"
				}
			}

			Bottom
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 0"
				}
				"2"
				{
					"color" "Border.Dark"
					"offset" "0 0"
				}
			}
		}

		FrameBorder
		{
			"inset" "0 0 1 1"
			Left
			{
				"1"
				{
					"color" "ControlBG"
					"offset" "0 1"
				}
			}

			Right
			{
				"1"
				{
					"color" "ControlBG"
					"offset" "0 0"
				}
			}

			Top
			{
				"1"
				{
					"color" "ControlBG"
					"offset" "0 1"
				}
			}

			Bottom
			{
				"1"
				{
					"color" "ControlBG"
					"offset" "0 0"
				}
			}
		}

		TabBorder
		{
			"inset" "0 0 1 1"
			Left
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "0 1"
				}
			}

			Right
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "1 0"
				}
			}

			Top
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "0 0"
				}
			}

			Bottom
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "0 0"
				}
			}
		}

		TabActiveBorder
		{
			"inset" "0 0 1 0"
			Left
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "0 0"
				}
			}

			Right
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "1 0"
				}
			}

			Top
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "0 0"
				}
			}

			Bottom
			{
				"1"
				{
					"color" "ControlBG"
					"offset" "6 2"
				}
			}
		}

		ToolTipBorder
		{
			"inset" "0 0 1 0"
			Left
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 0"
				}
			}

			Right
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "1 0"
				}
			}

			Top
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 0"
				}
			}

			Bottom
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 0"
				}
			}
		}

		// this is the border used for default buttons (the button that gets pressed when you hit enter)
		ButtonKeyFocusBorder
		{
			"inset" "0 0 0 0"
			Left
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "0 2"
				}
				"2"
				{
					"color" "Border.Bright"
					"offset" "0 2"
				}
			}

			Right
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 2"
				}
				"2"
				{
					"color" "Border.Dark"
					"offset" "0 2"
				}
			}

			Top
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "2 2"
				}
				"2"
				{
					"color" "Border.Bright"
					"offset" "2 2"
				}
			}

			Bottom
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 0"
				}
				"2"
				{
					"color" "Border.Dark"
					"offset" "0 0"
				}
			}
		}

		ButtonDepressedBorder
		{
			"inset" "0 0 0 0"
			Left
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "0 2"
				}
				"2"
				{
					"color" "Border.Bright"
					"offset" "0 2"
				}
			}

			Right
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 2"
				}
				"2"
				{
					"color" "Border.Dark"
					"offset" "0 2"
				}
			}

			Top
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "2 2"
				}
				"2"
				{
					"color" "Border.Bright"
					"offset" "2 2"
				}
			}

			Bottom
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 0"
				}
				"2"
				{
					"color" "Border.Dark"
					"offset" "0 0"
				}
			}
		}

		ComboBoxBorder
		{
			"inset" "0 0 0 0"
			Left
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "0 2"
				}
				"2"
				{
					"color" "Border.Bright"
					"offset" "0 2"
				}
			}

			Right
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 2"
				}
				"2"
				{
					"color" "Border.Dark"
					"offset" "0 2"
				}
			}

			Top
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "2 2"
				}
				"2"
				{
					"color" "Border.Bright"
					"offset" "2 2"
				}
			}

			Bottom
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 0"
				}
				"2"
				{
					"color" "Border.Dark"
					"offset" "0 0"
				}
			}
		}

		MenuBorder
		{
			"inset" "1 1 1 1"
			Left
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "0 1"
				}
			}

			Right
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "1 0"
				}
			}

			Top
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "0 0"
				}
			}

			Bottom
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 0"
				}
			}
		}
		BrowserBorder
		{
			"inset" "0 0 0 0"
			Left
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 0"
				}
			}

			Right
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "0 0"
				}
			}

			Top
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 0"
				}
			}

			Bottom
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "0 0"
				}
			}
		}

		BuyPresetBorder
		{
			"inset" "0 0 0 0"
			Left
			{
				"1"
				{
					"color" "Border.BuyPreset"
					"offset" "0 1"
				}
			}

			Right
			{
				"1"
				{
					"color" "Border.BuyPreset"
					"offset" "0 1"
				}
			}

			Top
			{
				"1"
				{
					"color" "Border.BuyPreset"
					"offset" "1 1"
				}
			}

			Bottom
			{
				"1"
				{
					"color" "Border.BuyPreset"
					"offset" "0 0"
				}
			}
		}

		BuyPresetButtonBorder
		{
			"inset" "0 0 0 0"
			Left
			{
				"1"
				{
					"color" "Blank"
					"offset" "0 1"
				}
			}

			Right
			{
				"1"
				{
					"color" "Border.Dark"
					"offset" "0 1"
				}
			}

			Top
			{
				"1"
				{
					"color" "Border.Bright"
					"offset" "1 1"
				}
			}

			Bottom
			{
				"1"
				{
					"color" "Blank"
					"offset" "0 0"
				}
			}
		}

		BlackBorder
		{
			"inset" "0 0 0 0"
			Left
			{
				"1"
				{
					"color" "Black"
					"offset" "0 1"
				}
			}

			Right
			{
				"1"
				{
					"color" "Black"
					"offset" "0 1"
				}
			}

			Top
			{
				"1"
				{
					"color" "Black"
					"offset" "1 1"
				}
			}

			Bottom
			{
				"1"
				{
					"color" "Black"
					"offset" "0 0"
				}
			}
		}

		HUDHealthArmorBarBorder
		{
			"inset" "0 0 1 1"
			Left
			{
				"1"
				{
					"color" "221 221 221 17"
					"offset" "0 1"
				}
			}

			Right
			{
				"1"
				{
					"color" "221 221 221 17"
					"offset" "1 0"
				}
			}

			Top
			{
				"1"
				{
					"color" "221 221 221 17"
					"offset" "0 0"
				}
			}

			Bottom
			{
				"1"
				{
					"color" "221 221 221 17"
					"offset" "0 0"
				}
			}
		}
	}

	//////////////////////// CUSTOM FONT FILES /////////////////////////////
	//
	// specifies all the custom (non-system) font files that need to be loaded to service the above described fonts
	CustomFontFiles
	{
		"1"				"resource/cs.ttf"
		"2"				"resource/csd.ttf"
		"3"				"resource/Cstrike.ttf"
		"4"				"resource/CSlogo.ttf"
		"5"
		{
			"font"		"resource/stratum2-regular.ttf"
			"name"		"Stratum2 Regular"
			"range"		"0x0000 0x04FF"
		}
		"6"
		{
			"font"		"resource/stratum2-bold.ttf"
			"name"		"Stratum2 Bold"
			"range"		"0x0000 0x04FF"
		}
		"7"				"resource/high-speed.ttf"
		"8"
		{
			"font"		"resource/notosans-regular.ttf"
			"name"		"Noto Sans"
			"range"		"0x0000 0x04FF"
		}
		"9"
		{
			"font"		"resource/stratum2-medium.ttf"
			"name"		"Stratum2 Medium"
			"range"		"0x0000 0x04FF"
		}
	}
}
