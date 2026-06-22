namespace CrestCreates.Agent.ControlPlane.Abstractions;

public interface IDescriptorReviewMessageTemplateCatalog
{
    string Format(string messageTemplateId, IReadOnlyDictionary<string, string> parameters);
    string TemplateVersion { get; }
}
