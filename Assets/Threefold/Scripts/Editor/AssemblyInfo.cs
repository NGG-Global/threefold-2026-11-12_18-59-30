using System.Runtime.CompilerServices;

// Lets the Edit Mode tests exercise internal Editor tooling such as the mesh builder.
[assembly: InternalsVisibleTo("Threefold.Tests.Editor")]
