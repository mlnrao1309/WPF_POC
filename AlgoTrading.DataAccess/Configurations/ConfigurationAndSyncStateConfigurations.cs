using AlgoTrading.Models.MarketData.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlgoTrading.DataAccess.Infrastructure.Configurations
{
    /// <summary>One resumption row per instrument and stream; the unique constraint makes duplicate chunks impossible.</summary>
    public sealed class IngestionSyncStateConfiguration : IEntityTypeConfiguration<IngestionSyncState>
    {
        public void Configure(EntityTypeBuilder<IngestionSyncState> builder)
        {
            builder.ToTable("IngestionSyncState", "dbo");
            builder.HasKey(entity => entity.Id).HasName("PK_IngestionSyncState");
            builder.Property(entity => entity.Id).ValueGeneratedOnAdd();
            builder.Property(entity => entity.InstrumentToken).IsRequired();
            builder.Property(entity => entity.StreamType).HasMaxLength(20).IsRequired();
            builder.Property(entity => entity.LastCompletedChunkStartDate).HasColumnType("datetime").IsRequired();
            builder.Property(entity => entity.LastCompletedChunkEndDate).HasColumnType("datetime").IsRequired();
            builder.Property(entity => entity.UpdatedUtc).HasColumnType("datetime").IsRequired();

            builder.HasIndex(entity => new { entity.InstrumentToken, entity.StreamType })
                .HasDatabaseName("UK_IngestionSyncState")
                .IsUnique();
        }
    }

    /// <summary>Config_Timeframes row; a deactivated row halts that timeframe without a deployment.</summary>
    public sealed class ConfigTimeframeConfiguration : IEntityTypeConfiguration<ConfigTimeframe>
    {
        public void Configure(EntityTypeBuilder<ConfigTimeframe> builder)
        {
            builder.ToTable("Config_Timeframes", "dbo");
            builder.HasKey(entity => entity.Id).HasName("PK_Config_Timeframes");
            builder.Property(entity => entity.Id).ValueGeneratedOnAdd();
            builder.Property(entity => entity.TimeframeCode).HasMaxLength(10).IsRequired();
            builder.Property(entity => entity.MinutesMultiplier).IsRequired();
            builder.Property(entity => entity.SourceStream).HasMaxLength(20).IsRequired();
            builder.Property(entity => entity.IsActive).IsRequired();

            builder.HasIndex(entity => entity.TimeframeCode)
                .HasDatabaseName("UQ_Config_Timeframes_TimeframeCode")
                .IsUnique();
        }
    }

    /// <summary>Config_CriticalLevels row: calculator, applied timeframe, parameters and sufficiency threshold.</summary>
    public sealed class ConfigCriticalLevelConfiguration : IEntityTypeConfiguration<ConfigCriticalLevel>
    {
        public void Configure(EntityTypeBuilder<ConfigCriticalLevel> builder)
        {
            builder.ToTable("Config_CriticalLevels", "dbo");
            builder.HasKey(entity => entity.Id).HasName("PK_Config_CriticalLevels");
            builder.Property(entity => entity.Id).ValueGeneratedOnAdd();
            builder.Property(entity => entity.MethodCode).HasMaxLength(50).IsRequired();
            builder.Property(entity => entity.AppliedTimeframe).HasMaxLength(10).IsRequired();
            builder.Property(entity => entity.ReferenceTimeframe).HasMaxLength(10);
            builder.Property(entity => entity.ParametersJson).HasColumnType("nvarchar(max)").IsRequired();
            builder.Property(entity => entity.MinimumBarsRequired).IsRequired();
            builder.Property(entity => entity.IsActive).IsRequired();
        }
    }
}
