using CrestCreates.Agent.Authoring.Abstractions.Authoring;

namespace CrestCreates.Agent.Authoring.Parsing;

public interface IDescriptorAuthoringOutputParser
{
    DescriptorAuthoringResult Parse(
        string providerOutputText,
        DescriptorAuthoringParseContext context);
}
