using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Entities;

namespace PubInvest.HouseConfig.Data;

public class HouseConfigDbContext(DbContextOptions<HouseConfigDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Submain> Submains => Set<Submain>();
    public DbSet<CircuitRow> Circuits => Set<CircuitRow>();
    public DbSet<ExtraFixtureRow> ExtraFixtures => Set<ExtraFixtureRow>();
    public DbSet<DeviceInstance> DeviceInstances => Set<DeviceInstance>();
    public DbSet<DeviceChannelRow> DeviceChannels => Set<DeviceChannelRow>();
    public DbSet<PanelRevision> PanelRevisions => Set<PanelRevision>();
    public DbSet<PositionOverrideRow> PositionOverrides => Set<PositionOverrideRow>();
    public DbSet<DeviceTypeRow> DeviceTypes => Set<DeviceTypeRow>();
    public DbSet<EnclosureTypeRow> Enclosures => Set<EnclosureTypeRow>();
    public DbSet<RuleSetRow> RuleSets => Set<RuleSetRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Project>().HasMany(p => p.Submains).WithOne(s => s.Project!)
            .HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.Cascade);

        // Panels built before this column existed all have an isolator, and so
        // does a submain created without saying either way.
        b.Entity<Submain>().Property(s => s.HasIsolator).HasDefaultValue(true);

        b.Entity<Submain>().HasMany(s => s.Circuits).WithOne()
            .HasForeignKey(c => c.SubmainId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Submain>().HasMany(s => s.ExtraFixtures).WithOne()
            .HasForeignKey(f => f.SubmainId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Submain>().HasMany(s => s.Devices).WithOne()
            .HasForeignKey(d => d.SubmainId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<DeviceInstance>().HasMany(d => d.Channels).WithOne()
            .HasForeignKey(c => c.DeviceInstanceId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<DeviceInstance>()
            .HasIndex(d => new { d.SubmainId, d.RowIndex, d.StartSlot }).IsUnique();

        b.Entity<DeviceChannelRow>()
            .HasIndex(c => new { c.DeviceInstanceId, c.ChannelIndex }).IsUnique();

        b.Entity<DeviceChannelRow>().HasIndex(c => c.CircuitId);

        b.Entity<PositionOverrideRow>().HasIndex(o => new { o.SubmainId, o.Label }).IsUnique();
        b.Entity<Submain>().HasMany<PositionOverrideRow>().WithOne()
            .HasForeignKey(o => o.SubmainId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<DeviceTypeRow>().HasIndex(d => d.PartNumber).IsUnique();
        b.Entity<RuleSetRow>().HasIndex(r => new { r.Name, r.Version }).IsUnique();
        b.Entity<PanelRevision>().HasIndex(r => new { r.SubmainId, r.LayoutVersion });

        foreach (var property in b.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            property.SetPrecision(12);
            property.SetScale(2);
        }

        // Tape wattage needs more resolution than money does.
        b.Entity<CircuitRow>().Property(c => c.WattsPerMetre).HasPrecision(8, 3);
        b.Entity<CircuitRow>().Property(c => c.LengthMetres).HasPrecision(8, 3);
    }
}
