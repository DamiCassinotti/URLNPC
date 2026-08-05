using System.Runtime.CompilerServices;

// The test assemblies drive internal seams (RunRng.ResetForNewRun,
// ArenaManager.suppressAutoBootstrap, TelemetryLogger.SwapWriter/RescanHealths,
// serialized config fields) that the game's public API deliberately does not
// expose.
[assembly: InternalsVisibleTo("URLNPC.Tests.EditMode")]
[assembly: InternalsVisibleTo("URLNPC.Tests.PlayMode")]
