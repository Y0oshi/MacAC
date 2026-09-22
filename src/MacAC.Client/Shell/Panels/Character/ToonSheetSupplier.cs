using MacAC.Mechanics.Avatar;
using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell.Panels;

public sealed partial class ToonSheetSupplier(
    ClientThingChart objects,
    SelfState localPlayer,
    Func<uint> playerGuid,
    Func<string?>? engagedToonLabel = null,
    Func<string, ToonSheet>? backupSheet = null,
    Func<bool>? canTransmitEmit = null,
    Action<uint, ulong>? transmitEmitAttr = null,
    Action<uint, ulong>? transmitEmitVital = null,
    Action<uint, ulong>? transmitEmitAptitude = null,
    Action<uint, uint>? transmitTrainAptitude = null,
    SimToonTitleLedger? banners = null,
    Func<uint, string?>? locateReadoutBanner = null,
    Func<string, string?>? locateWidgetString = null)
{
    private const uint UnassignedXpPropIdent = 2u;

    private static readonly uint[] AptitudeCreditPropIdents = [0x18u];

    private readonly ClientThingChart _objects = objects ?? throw new ArgumentNullException(nameof(objects));

    private readonly SelfState _ownAvatar = localPlayer ?? throw new ArgumentNullException(nameof(localPlayer));

    private readonly Func<uint> _avatarOid = playerGuid ?? throw new ArgumentNullException(nameof(playerGuid));

    private readonly Func<string?>? _engagedToonLabel = engagedToonLabel;

    private readonly Func<string, ToonSheet>? _backupSheet = backupSheet;

    private readonly Func<bool>? _canTransmitEmit = canTransmitEmit;

    private readonly Action<uint, ulong>? _transmitEmitAttr = transmitEmitAttr;

    private readonly Action<uint, ulong>? _transmitEmitVital = transmitEmitVital;

    private readonly Action<uint, ulong>? _transmitEmitAptitude = transmitEmitAptitude;

    private readonly Action<uint, uint>? _transmitTrainAptitude = transmitTrainAptitude;

    private readonly SimToonTitleLedger? _banners = banners;

    private readonly Func<uint, string?>? _locateReadoutBanner = locateReadoutBanner;

    private readonly Func<string, string?>? _locateWidgetString = locateWidgetString;

    private sealed class ChangeWiring : IDisposable
    {
        private ToonSheetSupplier? _holder;
        private readonly Action _altered;

        public ChangeWiring(ToonSheetSupplier holder, Action altered)
        {
            _holder = holder;
            _altered = altered;
            holder._objects.ObjectAdded += OnObjectAltered;
            holder._objects.ObjectUpdated += OnObjectAltered;
            holder._objects.ObjectRemoved += OnObjectAltered;
            holder._objects.Cleared += OnCleared;
            holder._ownAvatar.AttributeChanged += OnAttrAltered;
            holder._ownAvatar.CharacterChanged += OnToonAltered;
            holder._ownAvatar.Changed += OnVitalAltered;
            if (holder._ownAvatar.Spellbook is { } grimoire)
                grimoire.EnchantmentsChanged += OnCleared;
            if (holder._banners is { } banners)
            {
                banners.TableReplaced += OnCleared;
                banners.DisplayTitleChanged += OnReadoutBannerAltered;
            }
        }

        public void Dispose()
        {
            var holder = Interlocked.Exchange(ref _holder, null);
            if (holder is null)
                return;

            holder._objects.ObjectAdded -= OnObjectAltered;
            holder._objects.ObjectUpdated -= OnObjectAltered;
            holder._objects.ObjectRemoved -= OnObjectAltered;
            holder._objects.Cleared -= OnCleared;
            holder._ownAvatar.AttributeChanged -= OnAttrAltered;
            holder._ownAvatar.CharacterChanged -= OnToonAltered;
            holder._ownAvatar.Changed -= OnVitalAltered;
            if (holder._ownAvatar.Spellbook is { } grimoire)
                grimoire.EnchantmentsChanged -= OnCleared;
            if (holder._banners is { } banners)
            {
                banners.TableReplaced -= OnCleared;
                banners.DisplayTitleChanged -= OnReadoutBannerAltered;
            }
            holder.FreeExpectingEmit();
        }

        private void OnReadoutBannerAltered(uint _) => OnCleared();

        private void OnObjectAltered(ClientThing val)
        {
            var holder = _holder;
            if (holder is not null && val.ObjectId == holder._avatarOid())
            {
                holder.FreeExpectingEmit();
                _altered();
            }
        }

        private void OnCleared() => _altered();

        private void OnAttrAltered(SelfState.StatKind _)
        {
            _holder?.FreeExpectingEmit();
            _altered();
        }

        private void OnToonAltered()
        {
            _holder?.FreeExpectingEmit();
            _altered();
        }

        private void OnVitalAltered(SelfState.VitalSort _)
        {
            var holder = _holder;
            if (holder is null || !holder._expectingEmit)
                return;
            holder.FreeExpectingEmit();
            _altered();
        }
    }

    private bool _expectingEmit;
}
