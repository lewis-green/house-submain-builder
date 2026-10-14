using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Entities;

namespace PubInvest.HouseConfig.Data;

public class HouseConfigDbContext(DbContextOptions<HouseConfigDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Submain> Submains => Set<Submain>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<CircuitRow> Circuits => Set<CircuitRow>();
    public DbSet<ExtraFixtureRow> ExtraFixtures => Set<ExtraFixtureRow>();
    public DbSet<DeviceInstance> DeviceInstances => Set<DeviceInstance>();
    public DbSet<DeviceChannelRow> DeviceChannels => Set<DeviceChannelRow>();
    public DbSet<PanelRevision> PanelRevisions => Set<PanelRevision>();
    public DbSet<PositionOverrideRow> PositionOverrides => Set<PositionOverrideRow>();
    public DbSet<DeviceTypeRow> DeviceTypes => Set<DeviceTypeRow>();
    public DbSet<EnclosureTypeRow> Enclosures => Set<EnclosureTypeRow>();
    public DbSet<RuleSetRow> RuleSets => Set<RuleSetRow>();
    public DbSet<RackTypeRow> RackTypes => Set<RackTypeRow>();
    public DbSet<RackDeviceTypeRow> RackDeviceTypes => Set<RackDeviceTypeRow>();
    public DbSet<Rack> Racks => Set<Rack>();
    public DbSet<RackItem> RackItems => Set<RackItem>();
    public DbSet<RackPort> RackPorts => Set<RackPort>();
    public DbSet<RackRevision> RackRevisions => Set<RackRevision>();

    private const string CaseInsensitive = "case_insensitive";

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Project>().HasMany(p => p.Submains).WithOne(s => s.Project!)
            .HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Project>().HasMany<Location>().WithOne()
            .HasForeignKey(l => l.ProjectId).OnDelete(DeleteBehavior.Cascade);

        // "Plant room" and "plant room" are one room. A nondeterministic ICU
        // collation lets the unique index say so, where a plain index would let
        // both in; the endpoint refuses the duplicate first with a proper message.
        b.HasCollation(CaseInsensitive, locale: "und-u-ks-level2", provider: "icu", deterministic: false);
        b.Entity<Location>().Property(l => l.Name).UseCollation(CaseInsensitive);
        b.Entity<Location>().HasIndex(l => new { l.ProjectId, l.Name }).IsUnique();

        // A location is where something is, not what owns it: removing one
        // leaves its panels and racks in the house, unplaced.
        b.Entity<Submain>().HasOne<Location>().WithMany()
            .HasForeignKey(s => s.LocationId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Rack>().HasOne<Location>().WithMany()
            .HasForeignKey(r => r.LocationId).OnDelete(DeleteBehavior.SetNull);

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
        b.Entity<RackDeviceTypeRow>().HasIndex(d => d.PartNumber).IsUnique();

        b.Entity<Project>().HasMany<Rack>().WithOne()
            .HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Cascade);

        // The feed is a link, not ownership: if the submain ever goes, the rack
        // stays and simply has no feed recorded.
        b.Entity<Rack>().HasOne<Submain>().WithMany()
            .HasForeignKey(r => r.FedFromSubmainId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<Rack>().HasOne<RackTypeRow>().WithMany()
            .HasForeignKey(r => r.RackTypeId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<Rack>().Property(r => r.LayoutVersion).IsConcurrencyToken();

        b.Entity<Rack>().HasMany(r => r.Items).WithOne()
            .HasForeignKey(i => i.RackId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<RackItem>().HasIndex(i => new { i.RackId, i.TopU }).IsUnique();

        b.Entity<RackItem>().HasOne<RackDeviceTypeRow>().WithMany()
            .HasForeignKey(i => i.RackDeviceTypeId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<RackItem>().HasMany(i => i.Ports).WithOne()
            .HasForeignKey(p => p.RackItemId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<RackPort>().HasKey(p => new { p.RackItemId, p.Number });

        b.Entity<Rack>().HasMany<RackRevision>().WithOne()
            .HasForeignKey(r => r.RackId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RackRevision>().HasIndex(r => r.RackId);
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
