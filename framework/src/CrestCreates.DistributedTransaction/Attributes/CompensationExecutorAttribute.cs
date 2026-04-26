using System;

namespace CrestCreates.DistributedTransaction.Attributes
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class CompensationExecutorAttribute : Attribute
    {
    }
}
