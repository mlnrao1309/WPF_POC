using AlgoTrading.Models.MarketData.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlgoTrading.DataAccess.Infrastructure.Configurations
{
    /// <summary>CriticalLevels output table with the paged-read index required by the strategy readers.</summary>
    public sealed class CriticalLevelConfiguration : IEntityTypeConfiguration<CriticalLevel>
    {
        public void Configure(EntityTypeBuilder<CriticalLevel> builder)
        {
            builder.ToTable("CriticalLevels", "dbo");
            builder.HasKey(entity => entity.Id).HasName("PK_CriticalLevels");
            builder.Property(entity => entity.Id).ValueGeneratedOnAdd();
            builder.Property(entity => entity.InstrumentToken).IsRequired();
            builder.Property(entity => entity.TradingSymbol).HasMaxLength(50).IsRequired();
            builder.Property(entity => entity.Timeframe).HasMaxLength(10).IsRequired();
            builder.Property(entity => entity.LevelType).HasMaxLength(50).IsRequired();
            builder.Property(entity => entity.Price).HasColumnType("decimal(18,4)").IsRequired();
            builder.Property(entity => entity.LevelTimestamp).HasColumnType("datetime").IsRequired();
            builder.Property(entity => entity.ConfirmedAtTimestamp).HasColumnType("datetime").IsRequired();
            builder.Property(entity => entity.MethodCode).HasMaxLength(50).IsRequired();
            builder.Property(entity => entity.MetaDataJson).HasMaxLength(500);
            builder.Property(entity => entity.CreatedUtc).HasColumnType("datetime").IsRequired();

            builder.HasIndex(entity => new { entity.InstrumentToken, entity.Timeframe, entity.LevelTimestamp })
                .HasDatabaseName("IX_CriticalLevels_PagedRead")
                .IsDescending(false, false, true)
                .IncludeProperties(entity => new
                {
                    entity.TradingSymbol,
                    entity.LevelType,
                    entity.Price,
                    entity.ConfirmedAtTimestamp,
                    entity.MethodCode
                });
        }
    }
}
