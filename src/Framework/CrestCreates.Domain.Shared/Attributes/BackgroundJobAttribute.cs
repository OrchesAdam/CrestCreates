using System;

namespace CrestCreates.Domain.Shared.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class BackgroundJobAttribute : Attribute
    {
        /// <summary>Job display name. Defaults to class name.</summary>
        public string? Name { get; init; }

        /// <summary>Cron expression for recurring jobs. If empty/null, job is one-time/delayed.</summary>
        public string? CronExpression { get; init; }

        /// <summary>Whether authorization is required. Default false.</summary>
        public bool EnableAuthorization { get; init; }

        /// <summary>Job group name. Default "Default".</summary>
        public string Group { get; init; } = "Default";
    }
}
