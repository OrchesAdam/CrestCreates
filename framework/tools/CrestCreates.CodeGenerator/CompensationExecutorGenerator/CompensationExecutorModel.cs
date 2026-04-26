// framework/tools/CrestCreates.CodeGenerator/CompensationExecutorGenerator/CompensationExecutorModel.cs
using System.Collections.Generic;

namespace CrestCreates.CodeGenerator.CompensationExecutorGenerator
{
    internal sealed class CompensationExecutorModel
    {
        public string Namespace { get; set; } = string.Empty;
        public List<ExecutorInfo> Executors { get; set; } = new();
    }

    internal sealed class ExecutorInfo
    {
        public string ClassName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string NameProperty { get; set; } = string.Empty;
    }
}
