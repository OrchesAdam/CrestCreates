using System.Collections.Generic;

namespace CrestCreates.CodeGenerator.ObjectMappingGenerator
{
    /// <summary>
    /// Centralizes protected input field names that must not be overwritten
    /// by Create or Update DTO mapping (Apply direction).
    /// </summary>
    internal static class ObjectMappingProtectedFields
    {
        /// <summary>
        /// Fields that are always protected from input mapping.
        /// Includes tenant, audit, soft-delete, and identity fields.
        /// </summary>
        private static readonly HashSet<string> AlwaysProtected = new()
        {
            // Multi-tenancy
            "TenantId",

            // Soft delete
            "IsDeleted",
            "DeleterId",
            "DeletionTime",

            // Audit — modification
            "LastModificationTime",
            "LastModifierId",

            // Audit — creation
            "CreationTime",
            "CreatorId",

            // Identity
            "Id",
        };

        /// <summary>
        /// ConcurrencyStamp is protected only when not explicitly requested.
        /// Some update DTOs may intentionally include it for optimistic concurrency.
        /// </summary>
        public static bool IsProtectedInputField(string propertyName, bool includeConcurrencyStamp = true)
        {
            if (AlwaysProtected.Contains(propertyName))
                return true;

            if (includeConcurrencyStamp && propertyName == "ConcurrencyStamp")
                return true;

            return false;
        }

        public static bool IsProtectedInputProperty(string propertyName, bool includeConcurrencyStamp = true)
        {
            return IsProtectedInputField(propertyName, includeConcurrencyStamp);
        }
    }
}
