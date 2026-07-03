/// <summary>
/// Assembly marker for the Agent.Abstractions aggregate project.
/// This project serves as a dependency anchor — it provides a common reference point
/// for Agent sub-module abstractions (Memory, Authoring, Prompting, ControlPlane)
/// without carrying any public types. Sub-modules reference this project to establish
/// a shared identity within the Agent namespace.
/// </summary>
namespace CrestCreates.Agent.Abstractions;

public sealed class AssemblyMarker
{
}
