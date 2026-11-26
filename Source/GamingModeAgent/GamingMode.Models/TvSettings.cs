namespace GamingMode.Models;

// Eternal Core Mark III: LG webOS TV control (GamingMode.Services/TvControl.cs).
// Defaults must match the app's Playhub/Models/GamingModeConfig.cs (TvOptions).
// The pairing key is NOT here: config.json is plain text and rewritten by the app, so
// the key lives encrypted (DPAPI, current user) in %APPDATA%\GamingMode\tv-pairing.json.
public sealed class TvSettings
{
	public bool Enabled { get; set; }

	// Only "lg-webos" exists today; kept so another brand can be added without a migration.
	public string Brand { get; set; } = "lg-webos";

	public string? Address { get; set; }

	public string? MacAddress { get; set; }

	// The TV input Core is connected to: HDMI_1 to HDMI_4.
	public string CoreInput { get; set; } = "HDMI_2";

	// Wake the TV and switch it to CoreInput when Core boots or wakes into Gaming Mode.
	public bool TurnOnWithCore { get; set; } = true;

	// Turn the TV off when Core sleeps or shuts down, only while it shows CoreInput.
	public bool TurnOffWithCore { get; set; } = true;
}
