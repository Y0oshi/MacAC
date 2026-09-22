using MacAC.Dat;
using MacAC.Assets;
using MacAC.Client.Shell.Panels;

namespace MacAC.Client.Shell;

public sealed partial class CanonWidgetEngine
{
    public void PullJournal(string toonLabel) => DiaryFile.Load(toonLabel);

    private WidgetShortcutDigitGraphics PullShortcutDigitVisuals()
    {
        if (_shortcutDigitVisuals is not null)
            return _shortcutDigitVisuals;

        uint[]? regular = null, ghosted = null, vacant = null;
        lock (_bindings.Assets.DatLock)
        {
            var arrangement = _bindings.Assets.Dats.Get<UiLayout>(0x21000037u);
            if (arrangement is not null
                && arrangement.Elements.TryGetValue(0x10000346u, out var compound)
                && compound.Children.TryGetValue(0x1000034Au, out var number)
                && number.State?.Properties is { } props)
            {
                regular = ScanBlobIdents(props, 0x10000042u);
                ghosted = ScanBlobIdents(props, 0x10000043u);
            }
            if (arrangement is not null
                && arrangement.Elements.TryGetValue(0x10000341u, out var vacantCompound)
                && vacantCompound.Children.TryGetValue(0x1000034Au, out var vacantNumber)
                && vacantNumber.State?.Properties is { } vacantProps)
                vacant = ScanBlobIdents(vacantProps, 0x1000005Eu);
        }

        regular ??=
        [
            0x0600109Eu, 0x0600109Fu, 0x060010A0u, 0x060010A1u, 0x060010A2u,
            0x060010A3u, 0x060010A4u, 0x060010A5u, 0x060010A6u,
        ];
        ghosted ??=
        [
            0x06001ACCu, 0x06001ACDu, 0x06001ACEu, 0x06001ACFu, 0x06001AD0u,
            0x06001AD1u, 0x06001AD2u, 0x06001AD3u, 0x06001AD4u,
        ];
        _shortcutDigitVisuals = new WidgetShortcutDigitGraphics(
            regular, ghosted, vacant);
        Console.WriteLine(
            $"[UI] shared shortcut digits ready: regular={regular.Length}, " +
            $"ghosted={ghosted.Length}, empty={vacant?.Length ?? 0}.");
        return _shortcutDigitVisuals;
    }

    private CreditsWidgetResources? PullCreditsAssetList()
    {
        uint arrangementIdent;
        ElemDetails? pictureDetails;
        ElemDetails? phraseDetails;
        ImportedArrangement? pictureArrangement;
        ImportedArrangement? phraseArrangement;
        DatStringPicker texts = new DatStringPicker(_bindings.Assets.Dats);
        lock (_bindings.Assets.DatLock)
        {
            arrangementIdent = CanonDataIdResolver.Resolve(
                _bindings.Assets.Dats,
                CreditsWidgetDriver.TrunkEnum,
                5u);
            pictureDetails = arrangementIdent is 0u
                ? null
                : ArrangementLoader.ImportInfos(
                    _bindings.Assets.Dats,
                    arrangementIdent,
                    CreditsWidgetDriver.PictureTrunkElemIdent);
            phraseDetails = arrangementIdent is 0u
                ? null
                : ArrangementLoader.ImportInfos(
                    _bindings.Assets.Dats,
                    arrangementIdent,
                    CreditsWidgetDriver.PhraseTrunkElemIdent);
            pictureArrangement = arrangementIdent is 0u
                ? null
                : ArrangementLoader.Import(
                    _bindings.Assets.Dats,
                    arrangementIdent,
                    CreditsWidgetDriver.PictureTrunkElemIdent,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont);
            phraseArrangement = arrangementIdent is 0u
                ? null
                : ArrangementLoader.Import(
                    _bindings.Assets.Dats,
                    arrangementIdent,
                    CreditsWidgetDriver.PhraseTrunkElemIdent,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont);
        }

        if (pictureDetails is null
            || phraseDetails is null
            || pictureArrangement is null
            || phraseArrangement is null
            || !TryFetchCreditsBlobIdent(
                phraseDetails,
                0x10000002u,
                out uint phraseAreaIdent)
            || phraseAreaIdent != CreditsWidgetDriver.PhraseAreaElemIdent
            || !TryFetchCreditsBlobIdent(
                phraseDetails,
                0x10000003u,
                out uint stringChartIdent)
            || !phraseDetails.TryFetchNetFloat(
                0x10000004u,
                out float sectionSecs)
            || !pictureDetails.TryFetchNetProp(
                0x10000005u,
                out WidgetPropertyValue pictureProp)
            || pictureProp.Kind != WidgetPropertyKind.Array)
        {
            Console.WriteLine(
                "[UI] credits: enum-table-5 properties could not be imported");
            return null;
        }

        uint[] pictureIdents =
        [
            .. pictureProp.ArrayValue
                .Where(static val => val.Kind is
                    WidgetPropertyKind.DataId or WidgetPropertyKind.Enum)
                .Select(static val => checked((uint)val.UnsignedValue))
                .Where(static val => val != 0u),
        ];
        if (pictureIdents.Length is 0)
            return null;

        List<string> phraseFragments = new List<string>();
        string? pleasePause;
        lock (_bindings.Assets.DatLock)
        {
            for (int ordinal = 1; ordinal <= 4096; ++ordinal)
            {
                string? fragment = texts.Resolve(
                    stringChartIdent,
                    DatStringPicker.CalculateDigest($"ID_Credits{ordinal}"));
                if (fragment is null)
                    break;
                phraseFragments.Add(fragment);
            }

            pleasePause = texts.Resolve(
                0x23000001u,
                DatStringPicker.CalculateDigest("ID_Wait_PleaseWait"));
        }

        if (phraseFragments.Count is 0 || pleasePause is null)
        {
            Console.WriteLine(
                "[UI] credits: localized credit/wait strings are not available");
            return null;
        }

        Console.WriteLine(
            $"[UI] retail credits ready (layout 0x{arrangementIdent:X8}, "
            + $"{phraseFragments.Count} text fragments, {pictureIdents.Length} pictures). ");
        return new CreditsWidgetResources(
            arrangementIdent,
            pictureArrangement,
            phraseArrangement,
            phraseFragments,
            pictureIdents,
            sectionSecs,
            pleasePause);
    }

    private ToonManagementWidgetMountResources? PullToonManagementAssetList()
    {
        const uint stringChartIdent = 0x23000002u;
        uint arrangementIdent;
        ImportedArrangement? arrangement;
        DatStringPicker texts = new DatStringPicker(_bindings.Assets.Dats);
        lock (_bindings.Assets.DatLock)
        {
            arrangementIdent = CanonDataIdResolver.Resolve(
                _bindings.Assets.Dats,
                ToonManagementWidgetDriver.TrunkEnum,
                5u);
            arrangement = arrangementIdent is 0u
                ? null
                : ArrangementLoader.Import(
                    _bindings.Assets.Dats,
                    arrangementIdent,
                    ToonManagementWidgetDriver.TrunkElemIdent,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont);
        }

        if (arrangement is null)
        {
            Console.WriteLine(
                "[UI] character management: enum-table-5 root could not be imported");
            return null;
        }

        string? eraseResponse;
        string? eraseAckSensor;
        string? pleasePause;
        string? enteringRealm;
        string? confirmQuit;
        lock (_bindings.Assets.DatLock)
        {
            eraseAckSensor = texts.LocateBlueprint(
                stringChartIdent,
                "ID_CharacterManagement_DeleteCharacterConfirmation",
                new Dictionary<uint, string>
                {
                    [DatStringPicker.AvatarVariable] = string.Empty,
                });
            eraseResponse = LocateToonManagementString(
                texts,
                stringChartIdent,
                "ID_CharacterManagement_DeleteCharacterResponse");
            pleasePause = LocateToonManagementString(
                texts,
                stringChartIdent,
                "ID_CharacterManagement_PleaseWait");
            enteringRealm = LocateToonManagementString(
                texts,
                stringChartIdent,
                "ID_Character_EnteringWorld");
            confirmQuit = LocateToonManagementString(
                texts,
                stringChartIdent,
                "ID_CharacterManagement_ConfirmExit");
        }

        if (eraseAckSensor is null
            || eraseResponse is null
            || pleasePause is null
            || enteringRealm is null
            || confirmQuit is null)
        {
            Console.WriteLine(
                "[UI] character management: needed retail strings are not available");
            return null;
        }

        WidgetElem? LocateBlueprint(uint blueprintArrangementIdent, uint blueprintElemIdent)
        {
            lock (_bindings.Assets.DatLock)
            {
                return ArrangementLoader.Import(
                    _bindings.Assets.Dats,
                    blueprintArrangementIdent,
                    blueprintElemIdent,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont)?.Root;
            }
        }

        string ConstructEraseAck(string toonLabel)
        {
            lock (_bindings.Assets.DatLock)
            {
                return texts.LocateBlueprint(
                    stringChartIdent,
                    "ID_CharacterManagement_DeleteCharacterConfirmation",
                    new Dictionary<uint, string>
                    {
                        [DatStringPicker.AvatarVariable] = toonLabel,
                    })!;
            }
        }

        return new ToonManagementWidgetMountResources(
            arrangementIdent,
            arrangement,
            LocateBlueprint,
            new ToonManagementWidgetDriver.PromptStrings(
                ConstructEraseAck,
                eraseResponse,
                pleasePause,
                enteringRealm,
                confirmQuit),
            _bindings.Assets.DebugFont);
    }

    private ToonCreationWidgetMountResources? PullToonCreationAssetList()
    {
        const uint stringChartIdent = 0x23000002u;
        uint arrangementIdent;
        ImportedArrangement? arrangement;
        DatStringPicker texts = new DatStringPicker(_bindings.Assets.Dats);
        lock (_bindings.Assets.DatLock)
        {
            arrangementIdent = CanonDataIdResolver.Resolve(
                _bindings.Assets.Dats,
                ToonCreationWidgetDriver.TrunkEnum,
                5u);
            arrangement = arrangementIdent is 0u
                ? null
                : ArrangementLoader.Import(
                    _bindings.Assets.Dats,
                    arrangementIdent,
                    ToonCreationWidgetDriver.TrunkElemIdent,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont);
        }

        if (arrangement is null)
        {
            Console.WriteLine(
                "[UI] character creation: enum-table-5 root could not be imported");
            return null;
        }

        string? quitWarning;
        string? noLabelWarning;
        string? creditWarning;
        string? randomizeWarning;
        string? labelTooLong;
        lock (_bindings.Assets.DatLock)
        {
            quitWarning = LocateToonManagementString(
                texts,
                stringChartIdent,
                "ID_CharGen_ExitWarning");
            noLabelWarning = LocateToonManagementString(
                texts,
                stringChartIdent,
                "ID_CharGen_NoNameWarning");
            creditWarning = LocateToonManagementString(
                texts,
                stringChartIdent,
                "ID_CharGen_CreditWarning");
            randomizeWarning = LocateToonManagementString(
                texts,
                stringChartIdent,
                "ID_CharGen_RandomizeWarning");
            labelTooLong = LocateToonManagementString(
                texts,
                stringChartIdent,
                "ID_CharGen_NameTooLong");
        }
        if (quitWarning is null
            || noLabelWarning is null
            || creditWarning is null
            || randomizeWarning is null
            || labelTooLong is null)
        {
            Console.WriteLine(
                "[UI] character creation: needed retail strings are not available");
            return null;
        }

        WidgetElem? LocateBlueprint(uint blueprintArrangementIdent, uint blueprintElemIdent)
        {
            lock (_bindings.Assets.DatLock)
            {
                return ArrangementLoader.Import(
                    _bindings.Assets.Dats,
                    blueprintArrangementIdent,
                    blueprintElemIdent,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont)?.Root;
            }
        }

        return new ToonCreationWidgetMountResources(
            arrangementIdent,
            arrangement,
            LocateBlueprint,
            new ToonCreationWidgetDriver.PromptStrings(
                quitWarning, noLabelWarning, creditWarning, randomizeWarning, labelTooLong));
    }
}
