using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;

namespace WoWBuddy.Core.Tests.Fakes;

/// <summary>
/// Builds a <see cref="SimulatedClient"/> populated with a world.
/// </summary>
/// <remarks>
/// Every write goes through the same offset constants the production code reads, so the
/// resulting layout is the offset table's own description of a client, made concrete.
/// </remarks>
public sealed class SimulatedClientBuilder
{
    /// <summary>Bytes reserved per fake object. Comfortably past the furthest offset used.</summary>
    private const int ObjectSize = 0x1000;

    /// <summary>Bytes reserved per descriptor array. Past PLAYER_END at index 0x52E.</summary>
    private const int DescriptorSize = 0x1600;

    private readonly SimulatedClient _client = new();
    private readonly List<nint> _objects = [];
    private nint _manager;
    private WoWGuid _localPlayerGuid = WoWGuid.Zero;

    /// <summary>Where the fake character stands.</summary>
    public Vector3 PlayerPosition { get; set; } = new(-8913.23f, 554.63f, 93.79f);

    /// <summary>Creates the client-connection and object-manager structures.</summary>
    public SimulatedClientBuilder WithObjectManager()
    {
        nint connection = _client.Allocate(0x4000);
        _manager = _client.Allocate(0x400);

        _client.WritePointer(_client.ClientConnectionAddress, connection);
        _client.WritePointer(connection + (nint)Offsets335a.ObjectManager.CurMgr, _manager);
        return this;
    }

    /// <summary>
    /// Adds an object of <paramref name="type"/> with the given GUID.
    /// </summary>
    /// <param name="position">
    /// Where to place it. Written at the layout the fake client is configured for; ignored
    /// for object types that have no position.
    /// </param>
    public SimulatedClientBuilder WithObject(
        WoWObjectType type,
        WoWGuid guid,
        Vector3? position = null,
        Action<DescriptorWriter>? configureDescriptors = null)
    {
        nint address = _client.Allocate(ObjectSize);
        nint descriptors = _client.Allocate(DescriptorSize);

        _client.WriteUInt32(address + (nint)Offsets335a.Object.Type, (uint)type);
        _client.WriteUInt64(address + (nint)Offsets335a.Object.Guid, guid.Value);
        _client.WritePointer(address + (nint)Offsets335a.Object.Descriptors, descriptors);

        // The descriptor array carries the same GUID as the object. This is the relationship
        // the verifier relies on to prove the descriptor pointer offset is right.
        _client.WriteUInt64(descriptors + (nint)UpdateFields335a.ByteOffset(UpdateFields335a.Object.Guid), guid.Value);

        if (position is { } p)
        {
            _client.WriteVector3(address + (nint)_client.PositionLayout.PositionBlock, p);
            _client.WriteSingle(address + (nint)_client.PositionLayout.Facing, 0f);
        }

        configureDescriptors?.Invoke(new DescriptorWriter(_client, descriptors));

        _objects.Add(address);
        _client.ObjectAddresses.Add(address);
        return this;
    }

    /// <summary>Adds a plain creature with sensible descriptor values.</summary>
    public SimulatedClientBuilder WithCreature(ulong guidLow, uint entry, int level, Vector3 position)
    {
        var guid = new WoWGuid(guidLow | ((ulong)entry << 24) | ((ulong)WoWGuidType.Creature << 48));
        return WithObject(WoWObjectType.Unit, guid, position, d =>
        {
            d.WriteUInt32(UpdateFields335a.Unit.Level, (uint)level);
            d.WriteUInt32(UpdateFields335a.Unit.Health, 100);
            d.WriteUInt32(UpdateFields335a.Unit.MaxHealth, 100);
            d.WriteUInt32(UpdateFields335a.Object.Entry, entry);
        });
    }

    /// <summary>
    /// Adds the local player and records its GUID in the object manager header.
    /// </summary>
    public SimulatedClientBuilder WithLocalPlayer(
        ulong guidLow = 0x1234,
        int level = 80,
        uint health = 20000,
        uint maxHealth = 20000)
    {
        // Player GUIDs have a zero high word, which the verifier checks for.
        _localPlayerGuid = new WoWGuid(guidLow);

        WithObject(WoWObjectType.Player, _localPlayerGuid, PlayerPosition, d =>
        {
            d.WriteUInt32(UpdateFields335a.Unit.Level, (uint)level);
            d.WriteUInt32(UpdateFields335a.Unit.Health, health);
            d.WriteUInt32(UpdateFields335a.Unit.MaxHealth, maxHealth);
            d.WriteByte(UpdateFields335a.Unit.Bytes0, 0, 1);  // Human
            d.WriteByte(UpdateFields335a.Unit.Bytes0, 1, 4);  // Rogue
            d.WriteByte(UpdateFields335a.Unit.Bytes0, 3, 3);  // Energy
            d.WriteUInt32(UpdateFields335a.Unit.Power1 + 3, 100);
            d.WriteUInt32(UpdateFields335a.Unit.MaxPower1 + 3, 100);
        });

        if (_manager != 0)
        {
            _client.WriteUInt64(_manager + (nint)Offsets335a.ObjectManager.LocalPlayerGuid, _localPlayerGuid.Value);
        }

        return this;
    }

    /// <summary>Uses the second candidate position layout instead of the first.</summary>
    public SimulatedClientBuilder UsingPositionLayout(Offsets335a.PositionLayout layout)
    {
        _client.PositionLayout = layout;
        return this;
    }

    /// <summary>
    /// Links the objects into the manager's list and returns the finished client.
    /// </summary>
    /// <param name="terminator">
    /// What to write as the last object's next pointer. Zero is the normal terminator; an odd
    /// value is the other one the client uses.
    /// </param>
    public SimulatedClient Build(nint terminator = 0)
    {
        if (_manager == 0)
        {
            return _client;
        }

        if (_objects.Count == 0)
        {
            _client.WritePointer(_manager + (nint)Offsets335a.ObjectManager.FirstObject, 0);
            return _client;
        }

        _client.WritePointer(_manager + (nint)Offsets335a.ObjectManager.FirstObject, _objects[0]);

        for (int i = 0; i < _objects.Count; i++)
        {
            nint next = i + 1 < _objects.Count ? _objects[i + 1] : terminator;
            _client.WritePointer(_objects[i] + (nint)Offsets335a.ObjectManager.NextObject, next);
        }

        return _client;
    }

    /// <summary>Makes the last object's next pointer point back at the first, forming a cycle.</summary>
    public SimulatedClient BuildWithCycle()
    {
        SimulatedClient client = Build();
        if (_objects.Count > 0)
        {
            _client.WritePointer(
                _objects[^1] + (nint)Offsets335a.ObjectManager.NextObject, _objects[0]);
        }

        return client;
    }

    /// <summary>The local player's GUID, once one has been added.</summary>
    public WoWGuid LocalPlayerGuid => _localPlayerGuid;

    /// <summary>The object manager's address.</summary>
    public nint ManagerAddress => _manager;
}

/// <summary>Writes descriptor fields by index, mirroring how the client stores them.</summary>
public readonly struct DescriptorWriter
{
    private readonly SimulatedClient _client;
    private readonly nint _base;

    internal DescriptorWriter(SimulatedClient client, nint baseAddress)
    {
        _client = client;
        _base = baseAddress;
    }

    /// <summary>Writes a 32-bit descriptor field.</summary>
    public void WriteUInt32(uint index, uint value) =>
        _client.WriteUInt32(_base + (nint)UpdateFields335a.ByteOffset(index), value);

    /// <summary>Writes a single-precision descriptor field.</summary>
    public void WriteSingle(uint index, float value) =>
        _client.WriteSingle(_base + (nint)UpdateFields335a.ByteOffset(index), value);

    /// <summary>Writes a 64-bit descriptor field, which spans this index and the next.</summary>
    public void WriteGuid(uint index, WoWGuid guid) =>
        _client.WriteUInt64(_base + (nint)UpdateFields335a.ByteOffset(index), guid.Value);

    /// <summary>Writes one byte of a packed four-byte descriptor field.</summary>
    public void WriteByte(uint index, int byteIndex, byte value) =>
        _client.WriteByte(_base + (nint)UpdateFields335a.ByteOffset(index) + byteIndex, value);
}
