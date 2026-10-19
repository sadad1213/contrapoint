"Resource/HudLayout.res"
{
	HudHealthArmor
	{
		"fieldName"									"HudHealthArmor"
		"xpos"										"8"
		"ypos"										"r54"
		"zpos"										"0"
 		"wide"										"424"
		"tall"										"54"
		"visible"									"1"
		"enabled"									"1"

		"PaintBackgroundType"						"1"
		"bgcolor_override"							"White"
		"Texture1"									"vgui/hud/healtharmor_background"

		"health_icon_xpos"							"19"
		"health_icon_ypos"							"16"
		"health_icon_wide"							"22"
		"health_icon_tall"							"22"
		"health_icon_fgcolor"						"White"
		"health_bar_xpos"							"115"
		"health_bar_ypos"							"21"
		"health_bar_wide"							"82"
		"health_bar_tall"							"12"
		"health_bar_bgcolor"						"102 102 102 153"
		"health_bar_fgcolor"						"White"
		"health_xpos"								"82"
		"health_ypos"								"27"
		"health_font"								"HUDHealthArmorNumberFont"
		"health_fgcolor"							"White"
		"armor_icon_xpos"							"227"
		"armor_icon_ypos"							"16"
		"armor_icon_wide"							"20"
		"armor_icon_tall"							"20"
		"armor_icon_fgcolor"						"White"
		"armor_bar_xpos"							"322"
		"armor_bar_ypos"							"21"
		"armor_bar_wide"							"82"
		"armor_bar_tall"							"12"
		"armor_bar_bgcolor"							"102 102 102 153"
		"armor_bar_fgcolor"							"White"
		"armor_xpos"								"290"
		"armor_ypos"								"27"
		"armor_font"								"HUDHealthArmorNumberFont"
		"armor_fgcolor"								"White"
		"simple_xpos"								"8"
		"simple_ypos"								"r54"
		"simple_wide"								"244"
		"simple_tall"  								"54"
	}
	HudAmmo
	{
		"fieldName"									"HudAmmo"
		"xpos"										"r342"
		"ypos"										"r54"
		"wide"										"342"
		"tall"  									"54"
		"visible"									"1"
		"enabled"									"1"

		"PaintBackgroundType"						"1"
		"bgcolor_override"							"Black"
		"Texture1"									"vgui/hud/ammo_background"

		"bullet_icon_xpos"							"292"
		"bullet_icon_ypos"							"24"
		"bullet_icon_wide"							"16"
		"bullet_icon_tall"							"16"
		"bullet_icon_xrepeat"						"-10"
		"bullet_icon_fgcolor"						"255 255 255 230"
		"bullet_icon_low_fgcolor"					"255 0 0 230"
		"bullet_icon_count"							"5"
		"reserve_xpos"								"242"
		"reserve_ypos"								"20"
		"reserve_font"								"HUDAmmoReserveFont"
		"reserve_fgcolor"							"255 255 255 230"
		"clip_xpos"									"-9"	// relative to reserve
		"clip_ypos"									"0"
		"clip_font"									"HUDAmmoClipFont"
		"clip_fgcolor"								"White"
		"exhaustible_icon_xpos"						"285"
		"exhaustible_icon_ypos"						"9"
		"exhaustible_icon_wide"						"0"		// this has to be calculated procedurally
		"exhaustible_icon_tall"						"32"
		"exhaustible_icon_xrepeat"					"-32"
		"exhaustible_icon_fgcolor"					"255 255 255 230"
		"exhaustible_icon_count"					"3"
		"kill_icon_xpos"							"-23"	// relative to kill count
		"kill_icon_ypos"							"5"
		"kill_icon_wide"							"22"
		"kill_icon_tall"							"22"
		"kill_icon_xrepeat"							"-22"
		"kill_icon_count"							"5"
		"kill_count_xpos"							"120"
		"kill_count_ypos"							"4"
		"kill_count_font"							"HUDAmmoKillCountFont"
		"kill_count_fgcolor"						"White"
		"burst_icon_wide"							"20"
		"burst_icon_tall"							"0"		// this has to be calculated procedurally
		"burst_icon_ypos"							"7"
		"burst_icon_fgcolor"						"White"
		"simple_xpos"								"r282"
		"simple_ypos"								"r54"
		"simple_wide"								"282"
		"simple_tall"  								"54"
	}
	HudAccount
	{
		// keep in sync with MoneyLabel!!!
		"fieldName"									"HudAccount"
		"xpos"										"12"
		"ypos"										"350"
		"zpos"										"0"
 		"wide"										"166"
		"tall"										"48"
		"visible"									"1"
		"enabled"									"1"

		"PaintBackgroundType"						"1"
		"bgcolor_override"							"Black"
		"Texture1"									"vgui/hud/account_background"

		"account_xpos"								"19"
		"account_ypos"								"2"		// counts from center
		"account_fgcolor"							"White"
		"account_font"								"HUDAccountFont"
		"buy_icon_xpos"								"10"
		"buy_icon_wide"								"29"
		"buy_icon_tall"								"29"
		"buy_icon_fgcolor"							"White"
	}
	HudTeamCounter
	{
		"fieldName"									"HudTeamCounter"
		"xpos"										"0"
		"ypos"										"2"
		"zpos"										"0"
 		"wide"										"w1920"
		"tall"										"128"	// doesn't really matter, was 55 before, but it caused clipping issues on low res after adding min_gap_between_elements
		"visible"									"1"
		"enabled"									"1"

		"round_timer_xpos"							"c-42"
		"round_timer_ypos"							"0"
		"round_timer_wide"							"84"
		"round_timer_tall"							"27"
		"round_timer_fgcolor"						"White"
		"round_timer_low_fgcolor"					"255 0 0 255"
		"round_timer_bgcolor"						"0 0 0 153"
		"round_timer_font"							"HUDTeamCounterRoundFont"
		"bomb_icon_xpos"							"c5"	// center-aligned
		"bomb_icon_ypos"							"13"	// center-aligned
		"bomb_icon_wide"							"42"
		"bomb_icon_tall"							"0"		// this has to be calculated procedurally
		"bomb_icon_fgcolor"							"184 0 0 128"
		"bomb_icon_beep_fgcolor"					"184 0 0 255"
		"bomb_icon_defused_fgcolor"					"69 170 46 255"
		"wins_ct_xpos"								"c-42"
		"wins_ct_ypos"								"28"
		"wins_ct_wide"								"42"
		"wins_ct_tall"								"27"
		"wins_ct_fgcolor"							"TeamCT"
		"wins_ct_bgcolor"							"0 0 0 153"
		"wins_ct_font"								"HUDTeamCounterRoundFont"
		"wins_t_xpos"								"c1"
		"wins_t_ypos"								"28"
		"wins_t_wide"								"41"
		"wins_t_tall"								"27"
		"wins_t_fgcolor"							"TeamT"
		"wins_t_bgcolor"							"0 0 0 153"
		"wins_t_font"								"HUDTeamCounterRoundFont"
		"alive_count_ct_xpos"						"c-100"
		"alive_count_ct_ypos"						"0"
		"alive_count_ct_wide"						"55"
		"alive_count_ct_tall"						"55"
		"alive_count_t_xpos"						"c45"
		"alive_count_t_ypos"						"0"
		"alive_count_t_wide"						"55"
		"alive_count_t_tall"						"55"
		"alive_count_skull_wide"					"32"
		"alive_count_skull_tall"					"32"
		"alive_count_skull_fgcolor"					"204 0 51 178"
		"alive_count_number_xpos"					"27"	// center-aligned, relative to alive_count_(t/ct)_xpos
		"alive_count_number_ypos"					"24"	// center-aligned, relative to alive_count_(t/ct)_ypos
		"alive_count_number_fgcolor"				"White"
		"alive_count_number_font"					"HUDTeamCounterAliveCountNumberFont"
		"alive_count_label_xpos"					"27"	// center-aligned, relative to alive_count_(t/ct)_xpos
		"alive_count_label_ypos"					"46"	// center-aligned, relative to alive_count_(t/ct)_ypos
		"alive_count_label_fgcolor"					"White"
		"alive_count_label_font"					"HUDTeamCounterAliveCountLabelFont"
		"avatar_xmargin"							"2"		// margin between avatars, also starting xpos counting from round timer
		"avatar_ymargin"							"1"		// margin between avatars
		"avatar_wide"								"27"
		"avatar_tall"								"27"
		"avatar_xmax"								"6"
		"avatar_ymax"								"2"
		"avatar_border_size"						"2"
		"avatar_border_ct_fgcolor"					"TeamCT"
		"avatar_border_t_fgcolor"					"TeamT"
		"avatar_healthbar_tall"						"3"
		"avatar_healthbar_fgcolor"					"202 202 202 255"
		"avatar_healthbar_low_fgcolor"				"234 81 81 255"
		"avatar_dominated_wide"						"20"
		"avatar_dominated_tall"						"20"
		"avatar_dominated_fgcolor"					"White"
		"avatar_nemesis_wide"						"20"
		"avatar_nemesis_tall"						"20"
		"avatar_nemesis_fgcolor"					"White"
		"avatar_skull_wide"							"20"
		"avatar_skull_tall"							"20"
		"avatar_skull_fgcolor"						"71 84 95 178"
		"large_avatar_margin"						"2"		// margin between large avatars
		"large_avatar_wide"							"62"
		"large_avatar_tall"							"62"
		"large_avatar_max"							"10"
		"large_avatar_border_size"					"3"
		"large_avatar_healthbar_tall"				"4"
		"large_avatar_dominated_wide"				"54"
		"large_avatar_dominated_tall"				"54"
		"large_avatar_nemesis_wide"					"42"
		"large_avatar_nemesis_tall"					"42"
		"large_avatar_skull_wide"					"42"
		"large_avatar_skull_tall"					"42"
		"large_avatar_score_xpos"					"0"		// relative to large_avatar pos
		"large_avatar_score_ypos"					"0"		// relative to large_avatar pos
		"large_avatar_score_wide"					"62"
		"large_avatar_score_tall"					"24"
		"large_avatar_score_fgcolor"				"255 255 255 51"
		"large_avatar_score_self_fgcolor"			"White"
		"large_avatar_score_bgcolor"				"0 0 0 111"
		"large_avatar_score_font"					"HUDTeamCounterLargeAvatarScoreFont"
		"min_gap_between_elements"					"1"		// HACK FOR LOW RESOLUTION SCALING
	}
	HudDeathNotice
	{
		"fieldName"									"HudDeathNotice"
		"visible"									"1"
		"enabled"									"1"
		"RoundedCornerRadius1"						"4"
		"RoundedCornerRadius2"						"4"
		"RoundedCornerRadius3"						"4"
		"RoundedCornerRadius4"						"4"

		"right_side"								"1"		// whether kills are gonna be positioned to the right of the screen or not
		"entry_limit"								"16"
		"entry_xpos"								"10"
		"entry_ypos"								"75"
		"entry_wide"								"24"
		"entry_tall"								"32"
		"entry_margin"								"4"
		"entry_bgcolor"								"0 0 0 165"
		"entry_death_bgcolor"						"168 19 19 178"
		"entry_kill_border_color"					"225 0 0 255"
		"entry_kill_border_thick"					"2"
		"icon_wide"									"0"
		"icon_tall"									"24"
		"icon_inair_xpos"							"6"
		"icon_inair_ypos"							"-6"
		"name_font"									"HUDDeathNoticeNameFont"
		"name_t_fgcolor"							"234 190 84 255"
		"name_ct_fgcolor"							"111 156 230 255"
	}
	HudWeaponSelection
	{
		"fieldName"									"HudWeaponSelection"
		"visible"									"1"
		"enabled"									"1"

		"right_side"								"1"		// whether slots are gonna be positioned to the right of the screen or not
		"slot_xpos"									"6"		// these two represent the
		"slot_ypos"									"r104"	// left/right bottom corner
		"slot_ymargin"								"-62"	// margin between different slots
		"slot_font"									"HUDWeaponSelectionSlotFont"
		"slot_fgcolor"								"White"
		"weapon_icon_xpos"							"20"	// relative to slot
		"weapon_icon_ypos"							"43"	// relative to slot
		"weapon_icon_wide"							"0"		// this has to be calculated procedurally
		"weapon_icon_tall"							"48"
		"weapon_icon_xmirror"						"1"
		"weapon_icon_ymirror"						"0"
		"weapon_name_xpos"							"16"	// relative to slot
		"weapon_name_ypos"							"64"	// relative to slot
		"weapon_name_font"							"HUDWeaponSelectionNameFont"
		"weapon_name_fgcolor"						"White"
		"weapon_possession_font"					"HUDWeaponSelectionNameFont"
		"weapon_possession_fgcolor"					"173 173 173 255"
		"weapon_count_xpos"							"-7"	// relative to icon
		"weapon_count_ypos"							"31"	// relative to icon
		"weapon_count_font"							"HUDWeaponSelectionSlotFont"
		"defuser_icon_xpos"							"30"
		"defuser_icon_ypos"							"r50"
		"defuser_icon_wide"							"0"		// this has to be calculated procedurally
		"defuser_icon_tall"							"55"
		"defuser_icon_fgcolor"						"71 93 117 255"
		"defuser_icon_ymargin"						"-42"	// how many pixels to add to slot_ypos if the player has a defuser
	}
	HudInfo
	{
		"fieldName"									"HudInfo"
		"xpos"										"c-375"		// overriden by hud animations
		"ypos"										"r324"		// overriden by hud animations
		"zpos"										"0"
 		"wide"										"750"
		"tall"										"54"		// this does not include text height, it will be added procedurally
		"visible"									"1"
		"enabled"									"1"
		"Alpha"										"0"
		"font"										"HUDInfoTextFont"
		"textAlignment"								"south"
		"use_proportional_insets"					"1"
		"textinsety"								"-8"

		"PaintBackgroundType"						"1"
		"bgcolor_override"							"Black"
		"Texture1"									"vgui/hud/info_background"

		"border_texture"							"vgui/hud/info_border"
		"border_thick"								"1"
		"border_fgcolor"							"Black"
		"title_font"								"HUDInfoTitleFont"
		"title_ypos"								"8"
		"title_icon_wide"							"32"
		"title_icon_tall"							"32"
		"title_icon_margin"							"10"
		"alert_fgcolor"								"234 65 65 255"
		"hint_fgcolor"								"White"
	}
	HudProgressBar
	{
		"fieldName"									"HudProgressBar"
		"xpos"										"c-300"
		"ypos"										"c76"
		"zpos"										"0"
 		"wide"										"600"
		"tall"										"120"
		"visible"									"1"
		"enabled"									"1"

		"PaintBackgroundType"						"1"
		"bgcolor_override"							"Black"
		"Texture1"									"vgui/hud/progressbar_background"

		"border_texture"							"vgui/hud/progressbar_border"
		"border_thick"								"2"
		"border_fgcolor"							"Black"
	}
	HudDeathmatchBonus
	{
		"fieldName"									"HudDeathmatchBonus"
		"xpos"										"-400"	// overriden by hud animations
		"ypos"										"433"	// overriden by hud animations
		"zpos"										"0"
 		"wide"										"400"
		"tall"										"100"
		"visible"									"1"
		"enabled"									"1"

		"PaintBackgroundType"						"1"
		"bgcolor_override"							"Black"
		"Texture1"									"vgui/hud/deathmatchbonus_background"

		"border_texture"							"vgui/hud/deathmatchbonus_border"
		"border_thick"								"2"
		"border_fgcolor"							"Black"
		"weapon_icon_xpos"							"104"
		"weapon_icon_ypos"							"30"
		"weapon_icon_wide"							"0"		// this has to be calculated procedurally
		"weapon_icon_tall"							"64"
		"bonus_points_xpos"							"2"		// from the end of weapon icon
		"bonus_points_ypos"							"60"
		"bonus_points_font"							"HUDDeathmatchBonusPointsFont"
		"bonus_points_fgcolor"						"White"	// overriden by hud animations
		"bonus_weapon_xpos"							"104"
		"bonus_weapon_ypos"							"12"
		"bonus_weapon_font"							"HUDDeathmatchBonusWeaponFont"
		"bonus_weapon_fgcolor"						"221 221 221 255"
		"bonus_weapon_name_xpos"					"4"	// from the end of bonus weapon title
		"bonus_weapon_name_ypos"					"8"
		"bonus_weapon_name_font"					"HUDDeathmatchBonusWeaponNameFont"
		"bonus_weapon_name_fgcolor"					"White"
	}
	HudUniqueAlerts
	{
		"fieldName"									"HudUniqueAlerts"
		"xpos"										"c-440"
		"ypos"										"300"
		"zpos"										"0"
 		"wide"										"880"
		"tall"										"64"
		"visible"									"1"
		"enabled"									"1"
		"Alpha"										"0"
		"font"										"HUDUniqueAlertsFont"
		"textAlignment"								"center"

		"PaintBackgroundType"						"1"
		"bgcolor_override"							"Black"
		"Texture1"									"vgui/hud/uniquealerts_background"

		"border_texture"							"vgui/hud/uniquealerts_border"
		"border_thick"								"1"
		"border_fgcolor"							"Black"
		"text_fgcolor"								"243 243 243 255"
		"text_start_fgcolor"						"225 0 0 255"
	}
	HudTargetID
	{
		"fieldName"									"HudTargetID"
		"xpos"										"0"		// overriden by hud animations
		"ypos"										"c36"	// overriden by hud animations
		"zpos"										"0"
 		"wide"										"w1920"
		"tall"										"32"
		"visible"									"1"
		"enabled"									"1"
		"Alpha"										"0"
		"font"										"HUDTargetIDFont"
		"textAlignment"								"center"

		"player_fgcolor"							"255 204 0 255"
		"player_enemy_fgcolor"						"204 0 0 255"
		"player_teammate_fgcolor"					"110 125 92 255"
		"bot_requestbomb_fgcolor"					"229 218 37 255"
		"hostage_fgcolor"							"229 218 37 255"
		"weapon_fgcolor"							"White"
	}
	HudWinPanel
	{
		"fieldName"									"HudWinPanel"
		"xpos"										"0"
		"ypos"										"0"
		"zpos"										"0"
 		"wide"										"w1920"
		"tall"										"1080"
		"visible"									"1"
		"enabled"									"1"
		"Alpha"										"0"

		"teamlogo_xpos"								"c-44"
		"teamlogo_ypos"								"128"
		"teamlogo_wide"								"88"
		"teamlogo_tall"								"88"
	}
	CHudVote
	{
		"fieldName"									"CHudVote"
		"xpos"										"0"			
		"ypos"										"0"
		"wide"										"w1920"
		"tall"										"1080"
		"visible"									"1"
		"enabled"									"1"
		"bgcolor_override"							"0 0 0 0"
		"PaintBackgroundType"						"0"
	}
	overview
	{
		"fieldName"									"overview"
		"xpos"										"24"
		"ypos"										"80"
 		"wide"										"254"
		"tall"										"254"
		"visible"									"1"
		"enabled"									"1"
		"PaintBackgroundType"						"0"

		"radar_border_size"							"2"
		"radar_circle_overlay"						"vgui/hud/radar_circle_overlay"
		"radar_square_overlay"						"vgui/hud/radar_square_overlay"
		// these colors are not actually used (yet)
		"radar_color_t"								"255 221 35 255"
		"radar_color_ct"							"30 210 255 255"
		"radar_color_enemy"							"255 25 25 255"
		"radar_color_hostage"						"102 155 65 255"
		// but these ones are!
		"radar_bgcolor"								"0 0 0 128"
		"radar_bomb_bgcolor"						"255 0 0 25"
		"radar_bomb_beep_bgcolor"					"255 0 0 204"
		"vertical_section_switch_duration"			"0.25"
	}
	HudLocation
	{
		"fieldName"									"HudLocation"
		"xpos"										"24"
		"ypos"										"26"
 		"wide"										"w512"
		"tall"										"32"
		"visible"									"1"
		"enabled"									"1"
		"Alpha"										"179"
		"font"										"HUDLocationFont"
		"textAlignment"								"west"
		// WHICH ONE IS IT?? IT'S BOTH AT THE SAME TIME!!!
		// actually I don't care... I decided to hardcode it to current HUD color :)
		//"fgcolor_override"							"211 231 152 255"
		//"fgcolor_override"							"White"
	}
	HudFreezePanel
	{
		"fieldName"									"HudFreezePanel"
		"xpos"										"c-255"	// overriden by hud animations
		"ypos"										"-160"		// overriden by hud animations
 		"wide"										"510"
		"tall"										"160"
		"visible"									"1"
		"enabled"									"1"
		"Alpha"										"0"

		"PaintBackgroundType"						"1"
		"bgcolor_override"							"Black"
		"Texture1"									"vgui/hud/freezepanel_background"

		"killer_info_xpos"							"0"
		"killer_info_ypos"							"0"
		"killer_info_wide"							"510"
		"killer_info_tall"							"86"
		"killer_info_bgimage"						"vgui/hud/freezepanel_killer_background"
		"killer_avatar_xpos"						"0"		// relative to killer_info_xpos
		"killer_avatar_ypos"						"0"		// relative to killer_info_ypos
		"killer_avatar_wide"						"86"
		"killer_avatar_tall"						"86"
		"killer_avatar_border_size"					"3"
		"killer_avatar_border_image"				"vgui/hud/freezepanel_avatar_border"
		"killer_avatar_health_bar_tall"				"5"
		"killer_avatar_health_bar_border_size"		"1"
		"killer_avatar_health_bar_bgcolor"			"Black"
		"killer_avatar_health_bar_fgcolor"			"222 229 200 255"
		"killer_avatar_health_bar_border_fgcolor"	"194 197 186 128"
		"killer_name_xpos"							"96"	// relative to killer_info_xpos
		"killer_name_ypos"							"12"	// relative to killer_info_ypos
		"killer_name_fgcolor"						"204 204 204 255"
		"killer_name_font"							"HUDFreezePanelKillerNameFont"
		"killer_description_xpos"					"104"
		"killer_description_ypos"					"50"
		"killer_description_fgcolor"				"201 49 33 255"
		"killer_description_font"					"HUDFreezePanelKillerDescriptionFont"
		"killer_domination_image_xpos"				"456"
		"killer_domination_image_ypos"				"8"
		"killer_domination_image_wide"				"46"
		"killer_domination_image_tall"				"46"
		"damage_info_xpos"							"0"		// relative to bottom of killer_info
		"damage_info_ypos"							"0"		// relative to bottom of killer_info
		"damage_info_wide"							"510"
		"damage_info_tall"							"24"
		"damage_info_bgimage"						"vgui/hud/freezepanel_damage_background"
		"damage_info_fgcolor"						"160 160 160 255"
		"damage_info_font"							"HUDFreezePanelDamageFont"
		"damage_info_bold_fgcolor"					"White"
		"damage_info_bold_font"						"HUDFreezePanelDamageBoldFont"
		"damage_text_xinset"						"10"
		"damage_text_yinset"						"1" // this sucks!
		"screenshot_label_xpos"						"10"	// relative to last damage_info
		"screenshot_label_ypos"						"4"		// relative to last damage_info
		"screenshot_label_fgcolor"					"204 204 204 255"
		"screenshot_label_font"						"HUDFreezePanelScreenshotLabelFont"
	}
	HudDamageIndicator
	{
		"fieldName"									"HudDamageIndicator"
		"xpos"										"0"
		"ypos"										"0"
 		"wide"										"w1920"
		"tall"										"1080"
		"visible"									"1"
		"enabled"									"1"
		"fgcolor_override"							"204 51 0 255"
	}
	HudVoiceStatus
	{
		"fieldName"									"HudVoiceStatus"
		"xpos"										"0"
		"ypos"										"0"
 		"wide"										"w1920"
		"tall"										"1080"
		"visible"									"1"
		"enabled"									"1"

		"right_side"								"0"
		"reverse_speakers_list"						"0"
		"local_player_always_first"					"0"
		"fade_in_duration"							"0.25"
		"fade_out_duration"							"0.25"
		"fade_out_hold_duration"					"1.0"
		"speakers_xpos"								"32"	// starting position of first speaker
		"speakers_ypos"								"r120"	// starting position of first speaker
		"speakers_tall"								"22"	// each speaker's max height
		"speakers_margin"							"0"		// y position between speakers
		"speakers_max"								"3"
		"sound_icon_xpos"							"0"
		"sound_icon_ypos"							"0"
		"sound_icon_wide"							"22"
		"sound_icon_tall"							"22"
		"sound_icon_fgcolor"						"White"
		"avatar_xpos"								"22"	// relative to speakers
		"avatar_ypos"								"0"		// center-aligned to speakers
		"avatar_wide"								"22"
		"avatar_tall"								"22"
		"avatar_fgcolor"							"White"
		"avatar_dead_fgcolor"						"102 102 102 156"
		"avatar_skull_xpos"							"0"		// relative to center of avatar
		"avatar_skull_ypos"							"0"		// relative to center of avatar
		"avatar_skull_wide"							"16"
		"avatar_skull_tall"							"16"
		"avatar_skull_fgcolor"						"White"
		"avatar_draw_friend"						"0"
		"name_xpos"									"52"	// relative to speakers
		"name_ypos"									"0"		// center-aligned to speakers
		"name_font"									"HUDVoiceStatusNameFont"
		"name_location_fgcolor"						"0 255 0 255"
	}
	HudVoiceStatusDisabled
	{
		"fieldName"									"HudVoiceStatusDisabled"
		"xpos"										"22"
		"ypos"										"r78"
 		"wide"										"110"
		"tall"										"22"
		"visible"									"1"
		"enabled"									"1"
		"fgcolor_override"							"White"
		"labelText"									"#CStrike_HUD_VoiceStatus_Disabled"
		"font"										"HUDVoiceStatusDisabledFont"
		"textAlignment"								"west"
		"use_proportional_insets"					"1"
		"textinsetx"								"28"

		"PaintBackgroundType"						"1"
		"bgcolor_override"							"Black"
		"Texture1"									"vgui/hud/voicestatusdisabled_background"

		"icon_xpos"									"2"
		"icon_ypos"									"-1"
		"icon_wide"									"0"
		"icon_tall"									"24"
		"icon_fgcolor"								"255 255 255 178"
	}
	HudCommentary
	{
		"fieldName" "HudCommentary"
		"xpos"	"c-428"
		"ypos"	"789"
		"wide"	"855"
		"tall"  "90"
		"visible" "1"
		"enabled" "1"

		"PaintBackgroundType"	"2"

		"bar_xpos"		"113"
		"bar_ypos"		"45"
		"bar_height"	"18"
		"bar_width"		"720"
		"speaker_xpos"	"113"
		"speaker_ypos"	"18"
		"count_xpos_from_right"	"23"	// Counts from the right side
		"count_ypos"	"18"

		"icon_texture"	"vgui/hud/icon_commentary"
		"icon_xpos"		"0"
		"icon_ypos"		"0"
		"icon_width"	"90"
		"icon_height"	"90"
	}
	HudCrosshair
	{
		"fieldName" "HudCrosshair"
		"visible" "1"
		"enabled" "1"
		"wide"	 "1920"
		"tall"	 "1080"
	}
	HudVehicle
	{
		"fieldName" "HudVehicle"
		"visible" "1"
		"enabled" "1"
		"wide"	 "1920"
		"tall"	 "1080"
	}
	HudTrain
	{
		"fieldName" "HudTrain"
		"visible" "1"
		"enabled" "1"
		"wide"	 "1920"
		"tall"	 "1080"
	}
	HudMessage
	{
		"fieldName" "HudMessage"
		"visible" "1"
		"enabled" "1"
		"wide"	 "f0"
		"tall"	 "1080"
	}
	HudMenu
	{
		"fieldName" "HudMenu"
		"visible" "1"
		"enabled" "1"
		"wide"	 "1920"
		"tall"	 "1080"
		"zpos" "1"

		"TextFont"				"Default"
		"ItemFont"				"Default"
		"ItemFontPulsing"		"Default"
	}
	HudCloseCaption
	{
		"fieldName" "HudCloseCaption"
		"visible" "1"
		"enabled" "1"
		"wide"	 "1920"
		"tall"	 "1080"
	}
	HudGeiger
	{
		"fieldName" "HudGeiger"
		"visible" "1"
		"enabled" "1"
		"wide"	 "1920"
		"tall"	 "1080"
	}
	HudWeapon
	{
		"fieldName" "HudWeapon"
		"visible" "1"
		"enabled" "1"
		"wide"	 "1920"
		"tall"	 "1080"
	}
	HudAnimationInfo
	{
		"fieldName" "HudAnimationInfo"
		"visible" "1"
		"enabled" "1"
		"wide"	 "1920"
		"tall"	 "1080"
	}
	HudPredictionDump
	{
		"fieldName" "HudPredictionDump"
		"visible" "1"
		"enabled" "1"
		"wide"	 "1920"
		"tall"	 "1080"
	}
	HudScope
	{
		"fieldName" "HudScope"
		"visible" "1"
		"enabled" "1"
		"wide"	 "1920"
		"tall"	 "1080"
	}
	HudVoiceSelfStatus
	{
		"fieldName" "HudVoiceSelfStatus"
		"visible" "1"
		"enabled" "1"
		"xpos" "r77"
		"ypos" "799"
		"wide" "54"
		"tall" "54"
	}
	HudFlashbang
	{
	}
	"HudChat"
	{
		"ControlName"			"EditablePanel"
		"fieldName"				"HudChat"
		"visible" 				"1"
		"enabled" 				"1"
		"xpos"					"10"
		"ypos"					"r453"
		"wide"	 				"569"
		"tall"	 				"399"
		"PaintBackgroundType"	"0"
	}
	AchievementAnnouncePanel
	{
		"fieldName"				"AchievementAnnouncePanel"
		"visible"				"1"
		"enabled"				"1"
		"xpos"					"0"
		"ypos"					"0"
		"wide"					"f0"
		"tall"					"1080"
	}
	AchievementNotificationPanel
	{
		"fieldName"				"AchievementNotificationPanel"
		"visible"				"0"
		"enabled"				"1"
	}
	"HudAchievementTracker"
	{
		"ControlName"	"EditablePanel"
		"fieldName"		"HudAchievementTracker"
		"xpos"			"23"
		"NormalY"		"270"
		"EngineerY"		"383"
		"zpos"			"20"
		"wide"			"563"
		"tall"			"630"
		"visible"		"1"
		"enabled"		"1"
	}
}