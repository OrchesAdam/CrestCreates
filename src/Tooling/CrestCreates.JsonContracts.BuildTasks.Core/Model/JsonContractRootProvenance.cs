namespace CrestCreates.JsonContracts.BuildTasks.Model;

public sealed class JsonContractRootProvenance
{
    public string DeclaringSurface { get; set; } = string.Empty;
    public List<string> MethodSignatures { get; set; } = [];
    public bool IsReturnRoot { get; set; }
}
