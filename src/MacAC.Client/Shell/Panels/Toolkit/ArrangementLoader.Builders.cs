using MacAC.Dat;
using MacAC.Assets;

namespace MacAC.Client.Shell.Panels;

public static partial class ArrangementLoader
{

    public static ImportedArrangement AssembleFromInfos(
        ElemDetails trunkDetails,
        IEnumerable<ElemDetails> descendants,
        Func<uint, (uint, int, int)> locate,
        WidgetDatFont? datTypeface,
        Func<uint, WidgetDatFont?>? typefaceLocate = null,
        Func<WidgetStringInfoValue, string?>? stringLocate = null)
    {
        trunkDetails.Children = [.. descendants];
        return Build(trunkDetails, locate, datTypeface, typefaceLocate, stringLocate);
    }

    public static ImportedArrangement Build(
        ElemDetails trunkDetails,
        Func<uint, (uint, int, int)> locate,
        WidgetDatFont? datTypeface,
        Func<uint, WidgetDatFont?>? typefaceLocate = null,
        Func<WidgetStringInfoValue, string?>? stringLocate = null,
        uint srcArrangementDid = 0u)
    {
        var byIdent = new Dictionary<uint, WidgetElem>();
        var trunk = AssembleWidget(trunkDetails, locate, datTypeface, typefaceLocate, stringLocate, byIdent, srcArrangementDid);
        if (trunk is null)
        {
            Console.WriteLine($"[UI] ArrangementLoader: root element 0x{trunkDetails.Id:X8} (type {trunkDetails.Type}) produced no widget - using empty container fallback");
            trunk = new WidgetDatElement(trunkDetails, locate);
        }
        return new ImportedArrangement(trunk, byIdent);
    }

    public static ElemDetails? ImportInfos(IDatAccess datFiles, uint arrangementIdent)
    {
        UiLayout? desc = datFiles.Get<UiLayout>(arrangementIdent);
        if (desc is null) return null;

        HashSet<uint> referencedAsBase = new HashSet<uint>();
        foreach (var kv in desc.Elements)
            GatherBaseRefsInDsc(kv.Value, arrangementIdent, referencedAsBase);

        List<ElemDetails> tops = new List<ElemDetails>();
        foreach (var kv in desc.Elements)
        {
            LayoutNode d = kv.Value;
            if (referencedAsBase.Contains(d.ElementId) && HasNoOwnMedia(d))
            {
                Console.WriteLine($"[UI] ArrangementLoader: skipping prototype element 0x{d.ElementId:X8} in layout 0x{arrangementIdent:X8} (no own media, referenced as BaseElement)");
                continue;
            }

            tops.Add(Resolve(datFiles, d, []));
        }

        HashSet<uint> referencedAsBlueprint = new HashSet<uint>();
        foreach (ElemDetails top in tops)
            GatherBlueprintRefs(top, arrangementIdent, referencedAsBlueprint);
        if (referencedAsBlueprint.Count > 0)
        {
            for (int idx = tops.Count - 1; idx >= 0; --idx)
            {
                if (!referencedAsBlueprint.Contains(tops[idx].Id)) continue;
                Console.WriteLine(
                    $"[UI] ArrangementLoader: skipping row-template element 0x{tops[idx].Id:X8} "
                    + $"in layout 0x{arrangementIdent:X8} (referenced by a same-layout template list)");
                tops.RemoveAt(idx);
            }
        }

        if (tops.Count is 1)
            return tops[0];

        foreach (var top in tops)
            AssignOriginalAncestorDims(top, desc.Width, desc.Height);
        return new ElemDetails
        {
            Id = 0,
            Type = 3,
            Width = desc.Width,
            Height = desc.Height,
            Children = tops,
        };
    }

    public static ElemDetails? ImportInfos(
        IDatAccess datFiles,
        uint arrangementIdent,
        uint trunkElemIdent)
    {
        UiLayout? desc = datFiles.Get<UiLayout>(arrangementIdent);
        if (desc is null) return null;
        var trunk = SeekDsc(desc, trunkElemIdent);
        return trunk is null
            ? null
            : Resolve(datFiles, trunk, []);
    }

    public static ImportedArrangement? Import(
        IDatAccess datFiles,
        uint arrangementIdent,
        Func<uint, (uint, int, int)> locate,
        WidgetDatFont? datTypeface,
        Func<uint, WidgetDatFont?>? typefaceLocate = null)
    {
        ElemDetails? trunkDetails = ImportInfos(datFiles, arrangementIdent);
        if (trunkDetails is null) return null;
        DatStringPicker texts = new DatStringPicker(datFiles);
        return Build(trunkDetails, locate, datTypeface, typefaceLocate, texts.Resolve, arrangementIdent);
    }

    public static ImportedArrangement? Import(
        IDatAccess datFiles,
        uint arrangementIdent,
        uint trunkElemIdent,
        Func<uint, (uint, int, int)> locate,
        WidgetDatFont? datTypeface,
        Func<uint, WidgetDatFont?>? typefaceLocate = null)
    {
        ElemDetails? trunkDetails = ImportInfos(datFiles, arrangementIdent, trunkElemIdent);
        if (trunkDetails is null) return null;
        DatStringPicker texts = new DatStringPicker(datFiles);
        return Build(trunkDetails, locate, datTypeface, typefaceLocate, texts.Resolve, arrangementIdent);
    }

    internal static bool ShouldMountBaseDescendants(int derivedDescendantTally, int derivedMediaTally, int baseDescendantTally)
    {
        return derivedDescendantTally is 0 && derivedMediaTally is 0 && baseDescendantTally > 0;
    }

    internal static WidgetPropertyValue TranslateProp(PropertyValue prop)
    {
        WidgetPropertyValue val = new WidgetPropertyValue { MasterPropertyId = prop.PropertyId };
        switch (prop)
        {
            case EnumProperty p:
                val.Kind = WidgetPropertyKind.Enum;
                val.UnsignedValue = p.Value;
                break;
            case BoolProperty p:
                val.Kind = WidgetPropertyKind.Bool;
                val.BoolValue = p.Value;
                break;
            case DataIdProperty p:
                val.Kind = WidgetPropertyKind.DataId;
                val.UnsignedValue = p.Value;
                break;
            case FloatProperty p:
                val.Kind = WidgetPropertyKind.Float;
                val.FloatValue = p.Value;
                break;
            case IntegerProperty p:
                val.Kind = WidgetPropertyKind.Integer;
                val.IntegerValue = p.Value;
                break;
            case TextInfoProperty p:
                val.Kind = WidgetPropertyKind.StringInfo;
                val.StringInfoValue = new WidgetStringInfoValue(
                    p.Value.Token,
                    p.Value.StringId,
                    p.Value.TableId,
                    (byte)p.Value.Override,
                    p.Value.English,
                    p.Value.Comment);
                break;
            case ColorProperty p:
                val.Kind = WidgetPropertyKind.Color;
                val.ColorValue = new WidgetColorValue(
                    p.Value.Blue,
                    p.Value.Green,
                    p.Value.Red,
                    p.Value.Alpha);
                break;
            case ArrayProperty p:
                val.Kind = WidgetPropertyKind.Array;
                foreach (PropertyValue gear in p.Items)
                    val.ArrayValue.Add(TranslateProp(gear));
                break;
            case StructProperty p:
                val.Kind = WidgetPropertyKind.Struct;
                foreach (var (tag, gear) in p.Fields)
                    val.StructValue[tag] = TranslateProp(gear);
                break;
            case VectorProperty p:
                val.Kind = WidgetPropertyKind.Vector;
                val.VectorValue = p.Value;
                break;
            case Bitfield32Property p:
                val.Kind = WidgetPropertyKind.Bitfield32;
                val.UnsignedValue = p.Value;
                break;
            case Bitfield64Property p:
                val.Kind = WidgetPropertyKind.Bitfield64;
                val.UnsignedValue = p.Value;
                break;
            case InstanceIdProperty p:
                val.Kind = WidgetPropertyKind.InstanceId;
                val.UnsignedValue = p.Value;
                break;
            default:
                throw new NotSupportedException($"Not supported UI base-property type {prop.GetType().FullName}.");
        }

        return val;
    }

    private static WidgetElem? AssembleWidget(
        ElemDetails details,
        Func<uint, (uint, int, int)> locate,
        WidgetDatFont? datTypeface,
        Func<uint, WidgetDatFont?>? typefaceLocate,
        Func<WidgetStringInfoValue, string?>? stringLocate,
        Dictionary<uint, WidgetElem> byIdent,
        uint srcArrangementDid)
    {
        WidgetElem? element = DatWidgetMint.Create(details, locate, datTypeface, typefaceLocate, stringLocate);
        if (element is null) return null;               // Type-12 style prototype - skip

        element.SrcArrangementDid = srcArrangementDid;

        element.AuthoredInvisible = details.Invisible;
        if (details.Invisible)
            element.Visible = false;

        element.AuthoredHintTurnedOn = details.TooltipEnabled;
        element.AuthoredHintPhrase = DatWidgetMint.LocateHintPhrase(details, stringLocate);
        element.AuthoredHintTrunkElemIdent = details.TooltipRootElementId;
        element.AuthoredHintArrangementDid = details.TooltipLayoutDid;
        element.AuthoredHintPhraseDescendantElemIdent = details.TooltipTextChildElementId;
        element.AuthoredHintDelaySecs = details.TooltipDelaySeconds;

        element.AuthoredRescaleUpperWidth = details.MaxWidth;
        element.AuthoredRescaleLowerWidth = details.MinWidth;
        element.AuthoredRescaleUpperHeight = details.MaxHeight;
        element.AuthoredRescaleLowerHeight = details.MinHeight;

        if (details.Id is not 0) byIdent[details.Id] = element;

        if (!element.ConsumesDatChildren)
        {
            foreach (var descendant in details.Children)
            {
                WidgetElem? cw = AssembleWidget(descendant, locate, datTypeface, typefaceLocate, stringLocate, byIdent, srcArrangementDid);
                if (cw is not null) element.AddChild(cw);
            }
        }
        else if (element is WidgetGauge)
        {
            foreach (var descendant in details.Children)
            {
                if (descendant.Type is 3) continue;
                WidgetElem? cw = AssembleWidget(descendant, locate, datTypeface, typefaceLocate, stringLocate, byIdent, srcArrangementDid);
                if (cw is not null) element.AddChild(cw);
            }
        }
        else if (element is WidgetPhrase or WidgetField)
        {
            foreach (var descendant in details.Children)
            {
                if (descendant.StateMedia.Count is 0) continue;
                WidgetElem? cw = AssembleWidget(descendant, locate, datTypeface, typefaceLocate, stringLocate, byIdent, srcArrangementDid);
                if (cw is null) continue;
                element.AddChild(cw);
            }
        }

        if (element is IWidgetDatStateful stateful)
            stateful.TrySetCanonPhase(stateful.EngagedCanonPhaseIdent);

        if (element is IWidgetChildrenAttachedListener descendantsAffixed)
            descendantsAffixed.OnDescendantsAffixed();

        return element;
    }

    private static void GatherBlueprintRefs(
        ElemDetails details, uint arrangementIdent, HashSet<uint> referenced)
    {
        foreach (WidgetTemplateListEntry listing in details.TemplateList)
        {
            if (listing.TemplateLayoutId == arrangementIdent)
                referenced.Add(listing.TemplateElementId);
        }
        foreach (ElemDetails descendant in details.Children)
            GatherBlueprintRefs(descendant, arrangementIdent, referenced);
    }

    private static void GatherBaseRefsInDsc(LayoutNode desc, uint arrangementIdent, HashSet<uint> outcome)
    {
        if (desc.BaseElement is not 0 && desc.BaseLayoutId == arrangementIdent)
            outcome.Add(desc.BaseElement);
        foreach (var kv in desc.Children)
            GatherBaseRefsInDsc(kv.Value, arrangementIdent, outcome);
    }

    private static ElemDetails Resolve(
        IDatAccess datFiles,
        LayoutNode desc,
        HashSet<(uint layoutId, uint elementId)> baseChain)
    {
        ElemDetails self = ToDetails(desc);
        var outcome = self;
        ElemDetails? baseDetails = null;

        if (desc.BaseElement is not 0 && desc.BaseLayoutId is not 0
            && baseChain.Add((desc.BaseLayoutId, desc.BaseElement)))
        {
            UiLayout? baseLd = datFiles.Get<UiLayout>(desc.BaseLayoutId);
            LayoutNode? baseDsc = baseLd is null ? null : SeekDsc(baseLd, desc.BaseElement);
            if (baseDsc is not null)
            {
                baseDetails = Resolve(datFiles, baseDsc, baseChain);
                outcome = ElemScanner.Merge(baseDetails, self);
            }
        }

        IncorporateDescendants(datFiles, outcome, baseDetails?.Children, desc);

        if (baseDetails is not null
            && ShouldMountBaseDescendants(desc.Children.Count, self.StateMedia.Count, baseDetails.Children.Count))

            outcome.ZLevel = self.ZLevel;

        return outcome;
    }

    private static void IncorporateDescendants(
        IDatAccess datFiles,
        ElemDetails outcome,
        IReadOnlyList<ElemDetails>? baseDescendants,
        LayoutNode derived)
    {
        baseDescendants ??= Array.Empty<ElemDetails>();
        var baseByIdent = baseDescendants.ToDictionary(descendant => descendant.Id);
        int keptBaseTally = baseDescendants.Count(descendant =>
            !derived.Children.ContainsKey(descendant.Id));

        foreach (ElemDetails baseDescendant in baseDescendants)
        {
            bool hasTopLayer = derived.Children.TryGetValue(
                baseDescendant.Id, out LayoutNode? topLayer);
            ElemDetails child = hasTopLayer
                ? IncorporateSettledDescendant(datFiles, baseDescendant, topLayer!)
                : baseDescendant;
            if (hasTopLayer)
                AssignOriginalAncestorDims(child, outcome.Width, outcome.Height);
            outcome.Children.Add(child);
        }

        foreach (var duo in derived.Children)
        {
            if (baseByIdent.ContainsKey(duo.Key)) continue;
            ElemDetails child = Resolve(datFiles, duo.Value, []);
            child.ReadOrder += checked((uint)keptBaseTally);
            AssignOriginalAncestorDims(child, outcome.Width, outcome.Height);
            outcome.Children.Add(child);
        }
    }

    private static ElemDetails IncorporateSettledDescendant(
        IDatAccess datFiles,
        ElemDetails baseDescendant,
        LayoutNode derivedDescendant)
    {
        ElemDetails self = ToDetails(derivedDescendant);
        ElemDetails outcome = ElemScanner.Merge(baseDescendant, self);
        IncorporateDescendants(datFiles, outcome, baseDescendant.Children, derivedDescendant);
        return outcome;
    }

    private static ElemDetails ToDetails(LayoutNode desc)
    {
        string defPhase = desc.DefaultState.ToString();
        ElemDetails details = new ElemDetails
        {
            Id = desc.ElementId,
            Type = desc.Type,
            X = (float)desc.X,
            Y = (float)desc.Y,
            Width = (float)desc.Width,
            Height = (float)desc.Height,
            Left = desc.LeftEdge,
            Top = desc.TopEdge,
            Right = desc.RightEdge,
            Bottom = desc.BottomEdge,
            ReadOrder = desc.ReadOrder,
            ZLevel = desc.ZLevel,
            DefaultStateId = (uint)desc.DefaultState,
            DefaultStateName = (defPhase is "Undef" or "Undefined" or "0") ? "" : defPhase,
        };

        if (desc.State is not null)
            ScanPhase(desc.State, WidgetStateInfo.StraightPhaseIdent, "", details);

        foreach (var s in desc.States)
            ScanPhase(s.Value, (uint)s.Key, s.Key.ToString(), details);

        ElemScanner.ImposeCanonLegacyProj(details);
        return details;
    }

    private static void ScanPhase(UiState desc, uint phaseIdent, string label, ElemDetails details)
    {
        WidgetStateInfo phase = new WidgetStateInfo
        {
            Id = phaseIdent,
            Name = label,
            PassToChildren = desc.PassToChildren,
            IncorporationFlags = (uint)desc.Inherit,
            MediaCount = desc.Media.Count,
        };

        // Keep the WHOLE sequence, in order. A state's media is a small program - images interleaved with
        // pauses and jumps - and taking only the first image (below) reduces a blinking element to a still
        // frame.
        var hops = new List<WidgetMediaStep>(desc.Media.Count);
        foreach (UiMedia m in desc.Media)
        {
            hops.Add(m switch
            {
                UiImage idx => new WidgetMediaStep(
                    WidgetMediaStepKind.Image, idx.File, (int)idx.DrawMode, 0f, 0f, 0u, 0f),
                UiPause pause => new WidgetMediaStep(
                    WidgetMediaStepKind.Pause, 0u, 0, pause.MinDuration, pause.MaxDuration, 0u, 0f),
                UiStateJump st => new WidgetMediaStep(
                    WidgetMediaStepKind.State, 0u, 0, 0f, 0f,
                    (uint)st.StateId, st.Probability),
                UiJump jdx => new WidgetMediaStep(
                    WidgetMediaStepKind.Jump, 0u, 0, 0f, 0f, jdx.JumpItemIndex, jdx.Probability),
                _ => new WidgetMediaStep(
                    WidgetMediaStepKind.Other, 0u, 0, 0f, 0f, 0u, 0f, (int)m.Kind),
            });
        }
        phase.MediaSteps = hops;

        ScanPhaseRest(phase, phaseIdent, label, desc, details);
    }

    private static void ScanPhaseRest(WidgetStateInfo phase, uint phaseIdent, string label, UiState desc, ElemDetails details)
    {
        if (desc.Media.Count is 2
                && desc.Media[0] is UiAnimation anim
                && desc.Media[1] is UiJump { JumpItemIndex: 0, Probability: 1f }
                && anim.Frames.Count > 0
                && float.IsFinite(anim.Duration) && anim.Duration > 0f)
        {
            phase.LoopingAnimation = new WidgetLoopingImageMotion(
                [.. anim.Frames], anim.Duration, (int)anim.DrawMode);
        }
        bool imageScan = false;
        foreach (UiMedia m in desc.Media)
        {
            if (m is UiImage img)
            {
                phase.ImageMediaCount++;
                if (!imageScan && img.File is not 0)
                {
                    details.StateMedia[label] = (img.File, (int)img.DrawMode);
                    phase.Image = new WidgetImageMedia(img.File, (int)img.DrawMode);
                    imageScan = true;
                }
            }

            if (m is UiCursor cur && cur.File is not 0)
            {
                details.StateCursors[label] = new WidgetCursorMedia(
                    cur.File,
                    checked((int)cur.XHotspot),
                    checked((int)cur.YHotspot));
                phase.Cursor = details.StateCursors[label];
            }
        }
        ScanPhaseTail(phase, phaseIdent, desc, details);
    }

    private static void ScanPhaseTail(WidgetStateInfo phase, uint phaseIdent, UiState desc, ElemDetails details)
    {
        if (desc.Properties is not null)
        {
            foreach (var (propIdent, prop) in desc.Properties)
                phase.Properties.Values[propIdent] = TranslateProp(prop);
        }
        details.States[phaseIdent] = phase;
        if (details.FontDid is 0 && desc.Properties is not null
                    && desc.Properties.TryGetValue(0x1Au, out var raw)
                    && raw is ArrayProperty arr && arr.Items.Count > 0
                    && arr.Items[0] is DataIdProperty did)

            details.FontDid = did.Value;
        if (desc.Properties is not null)
        {
            if (details.FontColor is null
                && desc.Properties.TryGetValue(0x1Bu, out var cRaw)
                && cRaw is ColorProperty cProp)
            {
                Argb aRGB = cProp.Value;
                float a = aRGB.Alpha is 0 ? 1f : aRGB.Alpha / 255f;
                details.FontColor = new System.Numerics.Vector4(aRGB.Red / 255f, aRGB.Green / 255f, aRGB.Blue / 255f, a);
            }

            if (details.OutlineColor is null
                && desc.Properties.TryGetValue(0x22u, out var outlineTintRaw)
                && outlineTintRaw is ColorProperty outlineTintProp)
            {
                Argb oc = outlineTintProp.Value;
                float oa = oc.Alpha is 0 ? 1f : oc.Alpha / 255f;
                details.OutlineColor = new System.Numerics.Vector4(oc.Red / 255f, oc.Green / 255f, oc.Blue / 255f, oa);
            }
        }
    }

    private static bool HasNoOwnMedia(LayoutNode desc)
    {
        ElemDetails details = ToDetails(desc);
        return details.StateMedia.Count is 0;
    }

    private static LayoutNode? SeekDsc(UiLayout desc, uint ident)
    {
        foreach (var kv in desc.Elements)
        {
            LayoutNode? f = SeekDscIn(kv.Value, ident);
            if (f is not null) return f;
        }
        return null;
    }

    private static LayoutNode? SeekDscIn(LayoutNode desc, uint ident)
    {
        if (desc.ElementId == ident) return desc;
        foreach (var kv in desc.Children)
        {
            LayoutNode? f = SeekDscIn(kv.Value, ident);
            if (f is not null) return f;
        }
        return null;
    }

    private static void AssignOriginalAncestorDims(ElemDetails descendant, float width, float height)
    {
        descendant.OriginalParentWidth = width;
        descendant.OriginalParentHeight = height;
        descendant.HasOriginalParentSize = true;
    }
}
