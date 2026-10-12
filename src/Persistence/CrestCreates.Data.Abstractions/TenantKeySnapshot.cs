namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 租户键快照：区分「不参与租户校验」与「当前 host/null 身份」。
    /// </summary>
    /// <remarks>
    /// host/null 是有效的租户身份值，不能被当成关闭校验的开关：
    /// 两端均 <see cref="HasTenantSource"/> 时必须按值（含 null）精确比较。
    /// </remarks>
    public readonly struct TenantKeySnapshot
    {
        /// <summary>不参与租户校验（读方或帧没有租户来源）。</summary>
        public static TenantKeySnapshot NotParticipating => default;

        public TenantKeySnapshot(string? value)
        {
            HasTenantSource = true;
            Value = value;
        }

        /// <summary>是否具备租户来源（具备即参与比较，值可以为 null=host）。</summary>
        public bool HasTenantSource { get; }

        /// <summary>租户键值；null 表示 host（无租户）身份。</summary>
        public string? Value { get; }

        /// <summary>按值（含 null）精确比较。</summary>
        public bool Matches(TenantKeySnapshot other)
            => HasTenantSource && other.HasTenantSource &&
               string.Equals(Value, other.Value, System.StringComparison.Ordinal);
    }
}
