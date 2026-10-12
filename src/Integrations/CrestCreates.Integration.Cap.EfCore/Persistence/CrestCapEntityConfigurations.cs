using CrestCreates.DistributedTransaction.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrestCreates.Integration.Cap.EfCore.Persistence;

/// <summary>分布式事务日志的显式 EF Core 映射（应用在 OnModelCreating 显式应用；无 runtime 扫描）。</summary>
public sealed class TransactionLogConfiguration : IEntityTypeConfiguration<TransactionLog>
{
    public void Configure(EntityTypeBuilder<TransactionLog> builder)
    {
        builder.ToTable("CrestTransactionLogs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Message).HasMaxLength(1024);
        builder.Property(x => x.Metadata).HasMaxLength(2048);
        builder.Property(x => x.ErrorDetails);
        builder.HasIndex(x => x.Status);
    }
}

/// <summary>补偿记录的显式 EF Core 映射（应用在 OnModelCreating 显式应用；无 runtime 扫描）。</summary>
public sealed class TransactionCompensationConfiguration : IEntityTypeConfiguration<TransactionCompensation>
{
    public void Configure(EntityTypeBuilder<TransactionCompensation> builder)
    {
        builder.ToTable("CrestTransactionCompensations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ParticipantName).IsRequired().HasMaxLength(256);
        builder.Property(x => x.ErrorMessage).HasMaxLength(2048);
        builder.HasIndex(x => x.TransactionId);
        builder.HasIndex(x => x.Status);
    }
}
