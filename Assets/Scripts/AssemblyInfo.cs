using System.Runtime.CompilerServices;

// The test assemblies drive internal seams — reset hooks, bootstrap
// suppression, serialized config — that the public API deliberately withholds.
[assembly: InternalsVisibleTo("URLNPC.Tests.EditMode")]
[assembly: InternalsVisibleTo("URLNPC.Tests.PlayMode")]
