using System;

namespace CrestCreates.Domain.Shared.Settings;

[Flags]
public enum SettingScope
{
    Global = 1,
    Tenant = 2,
    User = 4
}
