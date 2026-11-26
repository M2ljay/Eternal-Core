namespace GamingMode.Models;

public sealed class GamingSplashSettings
{
	public bool AnimationEnabled { get; set; } = true;
	public string AnimationColor { get; set; } = "#D7263D";
	public double AnimationOpacity { get; set; } = 100;
	public bool AnimationUseCustomColor { get; set; }
	public double AnimationCustomHue { get; set; } = 47;
	public double AnimationCustomSaturation { get; set; } = 94;
	public double AnimationCustomBlackness { get; set; }
	public bool Enabled { get; set; } = true;

	public string? LogoPath { get; set; }

	public int MinVisibleMs { get; set; } = 1200;

	public int MaxVisibleMs { get; set; } = 120000;

	// Mark III boot sequence (BootSequenceVisual.cs) at power-on and wake. Defaults must
	// match the app's Playhub/Models/GamingModeConfig.cs (SplashOptions).
	public bool BootSequenceEnabled { get; set; } = true;

	public bool BootSoundEnabled { get; set; } = true;
}
