// Eternal Core Mark III tests (Linux-runnable): TV protocol and policy, Steam update hold,
// Steam client backup, Decky heartbeat health, boot sequence decisions, mode-switch transition
// timing and agent text.
int passed = 0;
void Check(bool condition, string name)
{
	if (!condition) throw new Exception("FAIL " + name);
	passed++;
	Console.WriteLine("PASS " + name);
}

await BootTests.Run(Check);
await TransitionTests.Run(Check);
await StartupMovieTests.Run(Check);
await TvTests.Run(Check);
await SteamHoldTests.Run(Check);
await DeckyTransitionTests.Run(Check);
await DeckyTests.Run(Check);
await CouchTests.Run(Check);

Console.WriteLine($"ALL PASS ({passed} checks)");
return 0;
