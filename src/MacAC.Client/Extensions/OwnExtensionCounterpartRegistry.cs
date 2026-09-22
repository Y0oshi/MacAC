using System.Text.Json;
using MacAC.Extensibility.Automation;

namespace MacAC.Client.Extensions;

internal sealed class OwnExtensionCounterpartRegistry : IDisposable
{
    internal static readonly TimeSpan StaleFollowing = TimeSpan.FromSeconds(15);
    private const long CeilingDocumentOctets = 64 * 1024;
    private static readonly JsonSerializerOptions JsonKnobs = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _directory;
    private readonly string _trail;
    private readonly TimeProvider _moment;
    private readonly Guid _instIdent;
    private bool _destroyed;

    public OwnExtensionCounterpartRegistry(
        string folder,
        TimeProvider? momentSupplier = null,
        Guid? instIdent = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        _directory = Path.GetFullPath(folder);
        _moment = momentSupplier ?? TimeProvider.System;
        _instIdent = instIdent ?? Guid.NewGuid();
        _trail = Path.Combine(_directory, $"peer-{_instIdent:N}.json");
        ClientId = BitConverter.ToUInt32(_instIdent.ToByteArray(), 0);
        if (ClientId is 0u)
            ClientId = 1u;
    }

    public uint ClientId { get; private set; }

    public void Publish(in PeerEntry client)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        Directory.CreateDirectory(_directory);
        CounterpartDoc document = CounterpartDoc.From(
            client with { ClientId = ClientId },
            _instIdent,
            _moment.GetUtcNow().ToUnixTimeMilliseconds());
        string temporary = _trail + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(document, JsonKnobs));
            File.Move(temporary, _trail, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public IReadOnlyList<PeerEntry> GrabDistantClients()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (!Directory.Exists(_directory))
            return Array.Empty<PeerEntry>();
        long newestAllowed = _moment.GetUtcNow().Subtract(StaleFollowing)
            .ToUnixTimeMilliseconds();
        List<PeerEntry> outcome = new List<PeerEntry>();
        foreach (string file in Directory.EnumerateFiles(
            _directory,
            "peer-*.json",
            SearchOption.TopDirectoryOnly))
        {
            if (file.Equals(_trail, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                FileInfo details = new FileInfo(file);
                if (details.Length is <= 0 or > CeilingDocumentOctets)
                    continue;
                var document = JsonSerializer.Deserialize<CounterpartDoc>(
                    File.ReadAllText(file),
                    JsonKnobs);
                if (document is null
                    || document.InstanceId == _instIdent
                    || document.UpdatedUnixMs < newestAllowed
                    || document.ClientId is 0u
                    || document.PlayerId is 0u
                    || string.IsNullOrWhiteSpace(document.Name)
                    || document.Name.Length > 128
                    || document.WorldName is null
                    || document.WorldName.Length > 128
                    || document.Tags is null
                    || document.Tags.Length > 128
                    || !double.IsFinite(document.EastWest)
                    || !double.IsFinite(document.NorthSouth)
                    || !double.IsFinite(document.Elevation)
                    || !float.IsFinite(document.Heading))

                    continue;
                outcome.Add(document.ToClient());
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (JsonException)
            {
            }
        }
        return outcome
            .OrderBy(static client => client.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static client => client.ClientId)
            .ToArray();
    }

    public void Withdraw()
    {
        if (_destroyed)
            return;
        try
        {
            if (File.Exists(_trail))
                File.Delete(_trail);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        Withdraw();
        _destroyed = true;
    }

    private sealed class CounterpartDoc
    {
        public Guid InstanceId { get; set; }
        public long UpdatedUnixMs { get; set; }
        public uint ClientId { get; set; }
        public uint PlayerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string WorldName { get; set; } = string.Empty;
        public string[] Tags { get; set; } = [];
        public uint CellId { get; set; }
        public double EastWest { get; set; }
        public double NorthSouth { get; set; }
        public double Elevation { get; set; }
        public bool IsOutdoor { get; set; }
        public float Heading { get; set; }
        public uint CurrentHealth { get; set; }
        public uint CurrentMana { get; set; }
        public uint CurrentStamina { get; set; }
        public uint MaxHealth { get; set; }
        public uint MaxMana { get; set; }
        public uint MaxStamina { get; set; }

        public static CounterpartDoc From(
            in PeerEntry client,
            Guid instIdent,
            long updatedUnixMsec)
        {
            return new()
            {
                InstanceId = instIdent,
                UpdatedUnixMs = updatedUnixMsec,
                ClientId = client.ClientId,
                PlayerId = client.PlayerId,
                Name = client.Name,
                WorldName = client.WorldName,
                Tags = [.. client.Tags
                .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                .Select(static tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(128)],
                CellId = client.Position.CellId,
                EastWest = client.Position.EastWest,
                NorthSouth = client.Position.NorthSouth,
                Elevation = client.Position.Elevation,
                IsOutdoor = client.Position.IsOutdoor,
                Heading = client.Heading,
                CurrentHealth = client.CurrentHealth,
                CurrentMana = client.CurrentMana,
                CurrentStamina = client.CurrentStamina,
                MaxHealth = client.MaxHealth,
                MaxMana = client.MaxMana,
                MaxStamina = client.MaxStamina,
            };
        }

        public PeerEntry ToClient()
        {
            return new(
            ClientId,
            PlayerId,
            Name,
            WorldName,
            new NavigationFix(
                CellId,
                EastWest,
                NorthSouth,
                Elevation,
                Heading,
                IsOutdoor),
            Tags,
            CurrentHealth,
            CurrentMana,
            CurrentStamina,
            MaxHealth,
            MaxMana,
            MaxStamina,
            Heading);
        }
    }
}
