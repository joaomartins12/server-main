using DigitalWorldOnline.Commons.DTOs.Config;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalWorldOnline.Infrastructure.ContextConfiguration.Config
{
    public class GlobalDropsConfiguration : IEntityTypeConfiguration<GlobalDropsConfigDTO>
    {
        public void Configure(EntityTypeBuilder<GlobalDropsConfigDTO> builder)
        {
            builder
                .ToTable("GlobalDrops", "Config")
                .HasKey(x => x.Id);

            builder
                .Property(x => x.ItemId)
                .HasColumnType("int")
                .IsRequired();

            builder
                .Property(x => x.MinDrop)
                .HasColumnType("tinyint")
                .IsRequired();
            builder
                .Property(x => x.MaxDrop)
                .HasColumnType("tinyint")
                .IsRequired();

            builder
                .Property(x => x.Chance)
                 .HasColumnType("numeric(9,2)")
                .HasDefaultValue(0)
                .IsRequired();

            builder
                .Property(x => x.Map)
                .HasColumnType("int")
                .IsRequired();

            builder
               .Property(x => x.StartTime)
               .HasColumnType("datetime2")
               .HasDefaultValueSql("getdate()")
               .IsRequired();

            builder
                .Property(x => x.EndTime)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("getdate()")
                .IsRequired();
        }
    }
}
