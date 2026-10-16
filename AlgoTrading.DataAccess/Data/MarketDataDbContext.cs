using Microsoft.EntityFrameworkCore;
using AlgoTrading.Models.MarketData.Entities;
using AlgoTrading.Models;
namespace AlgoTrading.DataAccess
{
    namespace Infrastructure.DatabaseContext
    {
        public class ApplicationDbContext : DbContext
        {
            public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
                : base(options) { }

            public DbSet<InstrumentEq> InstrumentsEq => Set<InstrumentEq>();
            public DbSet<InstrumentFo> InstrumentsFo => Set<InstrumentFo>();
            public DbSet<CriticalLevel> CriticalLevels => Set<CriticalLevel>();
            public DbSet<IngestionSyncState> IngestionSyncStates => Set<IngestionSyncState>();
            public DbSet<ConfigTimeframe> ConfigTimeframes => Set<ConfigTimeframe>();
            public DbSet<ConfigCriticalLevel> ConfigCriticalLevels => Set<ConfigCriticalLevel>();
            public DbSet<InstrumentBarView> InstrumentBars => Set<InstrumentBarView>();
            public DbSet<MarketWatch> MarketWatches => Set<MarketWatch>();
            public DbSet<InstrumentOhlc> InstrumentOhlc => Set<InstrumentOhlc>();

            public DbSet<InstrumentsCalculatedPivot> InstrumentsCalculatedPivots
    => Set<InstrumentsCalculatedPivot>();
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                base.OnModelCreating(modelBuilder);
                modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

                // Configure Instruments_EQ (Composite Primary Key)
                modelBuilder.Entity<InstrumentEq>(entity =>
                {
                    entity.ToTable("Instruments_EQ", "dbo");

                    entity.HasKey(e => new { e.InstrumentToken, e.Exchange })
                          .HasName("PK_Instruments_EQ");

                    entity.Property(e => e.Id).HasColumnName("ID").IsRequired();
                    entity.Property(e => e.InstrumentToken).HasColumnName("InstrumentToken").IsRequired();
                    entity.Property(e => e.TradingSymbol).HasColumnName("TradingSymbol").IsRequired();
                    entity.Property(e => e.Segment).HasColumnName("Segment").IsRequired();
                    entity.Property(e => e.Exchange).HasColumnName("Exchange").IsRequired();
                    entity.Property(e => e.LotSize).HasColumnName("LotSize").IsRequired();
                });

                // Configure Instruments_FO (Single Primary Key)
                modelBuilder.Entity<InstrumentFo>(entity =>
                {
                    entity.ToTable("Instruments_FO", "dbo");

                    entity.HasKey(e => e.InstrumentToken)
                          .HasName("PK_Instruments_FO");

                    entity.Property(e => e.Id).HasColumnName("ID").IsRequired();
                    entity.Property(e => e.InstrumentToken).HasColumnName("InstrumentToken").IsRequired();
                    entity.Property(e => e.TradingSymbol).HasColumnName("TradingSymbol").IsRequired();
                    entity.Property(e => e.Segment).HasColumnName("Segment").IsRequired();
                    entity.Property(e => e.Exchange).HasColumnName("Exchange").IsRequired();
                    entity.Property(e => e.LotSize).HasColumnName("LotSize").IsRequired(false);
                    entity.Property(e => e.Expiry).HasColumnName("expiry").IsRequired();
                });
                modelBuilder.Entity<MarketWatch>(entity =>
                {
                    entity.ToTable("MarketWatch");

                    // Composite PK
                    entity.HasKey(e => new { e.WatchId, e.Name, e.InstrumentToken })
                          .HasName("PK_MarketWatch");

                    // Idx is IDENTITY but not the PK
                    entity.Property(e => e.Idx)
                          .ValueGeneratedOnAdd()
                          .UseIdentityColumn();

                    entity.Property(e => e.Name)
                          .HasMaxLength(200)
                          .IsRequired();

                    entity.Property(e => e.TradingSymbol)
                          .HasMaxLength(50);

                    // nchar(10) / nchar(15) – fixed length Unicode
                    entity.Property(e => e.Segment)
                          .HasColumnType("nchar(10)")
                          .IsFixedLength();

                    entity.Property(e => e.Exchange)
                          .HasColumnType("nchar(10)")
                          .IsFixedLength();

                    entity.Property(e => e.FullId)
                          .HasColumnType("nchar(15)")
                          .IsFixedLength();

                    // datetime2(7)
                    entity.Property(e => e.InsertedOn).HasColumnType("datetime2(7)");
                    entity.Property(e => e.DeletedOn).HasColumnType("datetime2(7)");
                    entity.Property(e => e.ActiveInactiveOn).HasColumnType("datetime2(7)");
                    entity.Property(e => e.LastUpdateTime).HasColumnType("datetime2(7)");

                    // datetime for ExpiresOn
                    entity.Property(e => e.ExpiresOn).HasColumnType("datetime");

                    // Defaults
                    entity.Property(e => e.IsDeleted).HasDefaultValue(false);
                    entity.Property(e => e.IsActive).HasDefaultValue(true);
                    entity.Property(e => e.InsertedOn).HasDefaultValueSql("sysutcdatetime()");

                    // Global soft-delete filter (point 1)
                    entity.HasQueryFilter(e => !e.IsDeleted);
                });
                modelBuilder.Entity<InstrumentOhlc>(entity =>
                {
                    entity.ToTable("Instruments_OHLC", "dbo");
                    entity.HasKey(e => new { e.InstrumentToken, e.TimeFrame, e.TimeStamp });
                    entity.Property(e => e.TimeStamp).HasColumnType("datetime");
                    entity.Property(e => e.TimeFrame).HasColumnType("varchar(10)");
                    entity.Property(e => e.Open).HasColumnType("decimal(18,8)");
                    entity.Property(e => e.High).HasColumnType("decimal(18,8)");
                    entity.Property(e => e.Low).HasColumnType("decimal(18,8)");
                    entity.Property(e => e.Close).HasColumnType("decimal(18,8)");
                });

                modelBuilder.Entity<InstrumentBarView>(entity =>
                {
                    entity.HasNoKey();                             // view, no PK
                    entity.ToView("vw_InstrumentBars", "dbo");     // actual view name — adjust

                    modelBuilder.Entity<InstrumentBarView>(entity =>
                    {
                        entity.HasNoKey();
                        entity.ToView("vw_InstrumentBars", "dbo");   // your actual view name

                        entity.Property(e => e.InstrumentToken).HasColumnType("bigint");

                        entity.Property(e => e.Open).HasColumnType("decimal(18,8)");
                        entity.Property(e => e.High).HasColumnType("decimal(18,8)");
                        entity.Property(e => e.Low).HasColumnType("decimal(18,8)");
                        entity.Property(e => e.Close).HasColumnType("decimal(18,8)");

                        entity.Property(e => e.Volume).HasColumnType("bigint");        // ← was the missing piece
                        entity.Property(e => e.OI).HasColumnType("bigint");            // ← or whatever the DB says

                        entity.Property(e => e.TimeStamp).HasColumnType("datetime");
                        entity.Property(e => e.LastUpdated).HasColumnType("datetime2(7)");
                        entity.Property(e => e.ExpiresOn).HasColumnType("datetime");
                        entity.Property(e => e.TimeFrame).HasColumnType("varchar(10)");

                        entity.Property(e => e.Segment).HasColumnType("nchar(10)").IsFixedLength();
                        entity.Property(e => e.Exchange).HasColumnType("nchar(10)").IsFixedLength();
                        entity.Property(e => e.TradingSymbol).HasMaxLength(50);
                    });
                });

                modelBuilder.Entity<InstrumentsCalculatedPivot>(entity =>
                {
                    entity.ToTable("Instruments_CalculatedPivots", "dbo");

                    // Composite primary key — matches the clustered PK on the table.
                    // Note: the DB PK is (InstrumentToken ASC, TimeStamp DESC, TimeFrame ASC).
                    // EF will still treat it as an equality-only key; DESC is a storage hint
                    // and does not need to be modelled.
                    entity.HasKey(e => new { e.InstrumentToken, e.TimeStamp, e.TimeFrame })
                          .HasName("PK_Instruments_CalculatedPivots");

                    entity.Property(e => e.TimeStamp).HasColumnType("datetime2(3)");
                    entity.Property(e => e.TimeFrame).HasColumnType("varchar(10)").HasMaxLength(10);

                    entity.Property(e => e.Open).HasColumnType("decimal(18,4)");
                    entity.Property(e => e.High).HasColumnType("decimal(18,4)");
                    entity.Property(e => e.Low).HasColumnType("decimal(18,4)");
                    entity.Property(e => e.Close).HasColumnType("decimal(18,4)");

                    entity.Property(e => e.PP).HasColumnType("decimal(18,4)");
                    entity.Property(e => e.R1).HasColumnType("decimal(18,4)");
                    entity.Property(e => e.R2).HasColumnType("decimal(18,4)");
                    entity.Property(e => e.R3).HasColumnType("decimal(18,4)");
                    entity.Property(e => e.R4).HasColumnType("decimal(18,4)");
                    entity.Property(e => e.R5).HasColumnType("decimal(18,4)");
                    entity.Property(e => e.S1).HasColumnType("decimal(18,4)");
                    entity.Property(e => e.S2).HasColumnType("decimal(18,4)");
                    entity.Property(e => e.S3).HasColumnType("decimal(18,4)");
                    entity.Property(e => e.S4).HasColumnType("decimal(18,4)");
                    entity.Property(e => e.S5).HasColumnType("decimal(18,4)");

                    entity.Property(e => e.Ref_Resistance).HasColumnType("decimal(18,4)");
                    entity.Property(e => e.Ref_Support).HasColumnType("decimal(18,4)");

                    entity.Property(e => e.Ref_StartCandle_Open).HasColumnType("decimal(18,4)");
                    entity.Property(e => e.Ref_StartCandle_High).HasColumnType("decimal(18,4)");
                    entity.Property(e => e.Ref_StartCandle_Low).HasColumnType("decimal(18,4)");
                    entity.Property(e => e.Ref_StartCandle_Close).HasColumnType("decimal(18,4)");
                    entity.Property(e => e.Ref_EndCandle_Open).HasColumnType("decimal(18,4)");
                    entity.Property(e => e.Ref_EndCandle_High).HasColumnType("decimal(18,4)");
                    entity.Property(e => e.Ref_EndCandle_Low).HasColumnType("decimal(18,4)");
                    entity.Property(e => e.Ref_EndCandle_Close).HasColumnType("decimal(18,4)");

                    entity.Property(e => e.CreatedUtc).HasColumnType("datetime2(3)");
                });
            }
        }
    }
}
