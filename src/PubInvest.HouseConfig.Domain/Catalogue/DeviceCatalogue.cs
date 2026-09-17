namespace PubInvest.HouseConfig.Domain.Catalogue;

public sealed class DeviceCatalogue
{
    private readonly Dictionary<Guid, DeviceType> _byId;

    public DeviceCatalogue(IEnumerable<DeviceType> deviceTypes)
        => _byId = deviceTypes.ToDictionary(d => d.Id);

    public DeviceType? Find(Guid id) => _byId.GetValueOrDefault(id);

    public DeviceType? FindActive(Guid id)
        => _byId.TryGetValue(id, out var d) && d.Active ? d : null;

    public IReadOnlyCollection<DeviceType> All => _byId.Values;
}
