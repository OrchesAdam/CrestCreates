using Microsoft.EntityFrameworkCore;

namespace CrestCreates.Data.EFCore.MultiTenancy
{
    /// <summary>
    /// Applies compile-time generated soft-delete filters. Missing registrations only mean the
    /// project declares no soft-delete entities; a declared entity without a generated filter is a
    /// generator/consumer defect covered by the model filter tests.
    /// </summary>
    public static class SoftDeleteFilterConfiguration
    {
        public static void ApplyAll(ModelBuilder modelBuilder)
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);

            foreach (var applyAll in SoftDeleteFilterRegistryStore.GetApplyAllDelegates())
            {
                applyAll(modelBuilder);
            }
        }
    }
}
