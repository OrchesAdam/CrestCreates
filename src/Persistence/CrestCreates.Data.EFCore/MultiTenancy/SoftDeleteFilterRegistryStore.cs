using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.Data.EFCore.MultiTenancy
{
    /// <summary>
    /// Runtime registry for compile-time generated soft-delete filters. Each delegate is a
    /// strongly typed per-entity configuration guarded by the target context model.
    /// </summary>
    public static class SoftDeleteFilterRegistryStore
    {
        public delegate void ApplyAllDelegate(ModelBuilder modelBuilder);

        private static readonly ConcurrentDictionary<string, ApplyAllDelegate> ApplyAllDelegates = new(StringComparer.Ordinal);

        public static void Register(ApplyAllDelegate applyAll)
        {
            ArgumentNullException.ThrowIfNull(applyAll);
            ApplyAllDelegates.TryAdd(
                applyAll.Method.DeclaringType?.AssemblyQualifiedName ?? applyAll.Method.Name,
                applyAll);
        }

        public static bool HasRegistrations => !ApplyAllDelegates.IsEmpty;

        public static IReadOnlyCollection<ApplyAllDelegate> GetApplyAllDelegates()
        {
            return ApplyAllDelegates.Values.ToArray();
        }

        internal static void Clear()
        {
            ApplyAllDelegates.Clear();
        }
    }
}
