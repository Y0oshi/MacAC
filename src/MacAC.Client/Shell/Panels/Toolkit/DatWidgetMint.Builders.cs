namespace MacAC.Client.Shell.Panels;

public static partial class DatWidgetMint
{
    private static WidgetMenu AssembleMenu(
        ElemDetails details,
        Func<uint, (uint, int, int)> locate,
        WidgetDatFont? elemTypeface,
        Func<uint, WidgetDatFont?>? typefaceLocate)
    {
        var caption = details.Children.FirstOrDefault(
            static descendant => descendant.Type == 12u);
        WidgetDatFont? captionTypeface = caption is { FontDid: not 0u } && typefaceLocate is not null
            ? typefaceLocate(caption.FontDid) ?? elemTypeface
            : elemTypeface;
        WidgetMenu menu = new WidgetMenu
        {
            SpriteResolve = locate,
            DatFont = captionTypeface,
            BtnDatTypeface = captionTypeface,
            NormSprite = 0x06004D65u,
            PressedSprite = 0x06004D66u,
            PopupBgSprite = 0x0600124Cu,
            GearNormSprite = 0x0600124Eu,
            GearHighlightSprite = 0x0600124Du,
            BtnPhraseCentered = caption?.HJustify == ClientHJustify.Center,
        };
        if (caption?.FontColor is { } tint)
            menu.WordingTint = tint;
        return menu;
    }

    private static WidgetScroller AssembleScroller(
        ElemDetails details,
        Func<uint, (uint tex, int w, int h)> locate)
    {
        WidgetScroller bar = new WidgetScroller
        {
            SpriteResolve = locate,
            FollowSprite = DefaultImage(details),
            Horizontal = details.Width > details.Height,
        };

        uint incrementIdent = ReferencedElemIdent(details, 0x77u);
        uint decrementIdent = ReferencedElemIdent(details, 0x78u);
        var increment = details.Children.FirstOrDefault(descendant => descendant.Id == incrementIdent);
        var decrement = details.Children.FirstOrDefault(descendant => descendant.Id == decrementIdent);

        var leadingBtn = increment;
        var trailingBtn = decrement;
        if (leadingBtn is null && trailingBtn is null)
        {
            ElemDetails[] kindOneDescendants = [.. details.Children
                .Where(descendant => descendant.Type is 1u && descendant.Id is not 1u)
                .OrderBy(descendant => bar.Horizontal ? descendant.X : descendant.Y)
                .ThenBy(descendant => descendant.ReadOrder)];
            leadingBtn = kindOneDescendants.FirstOrDefault();
            trailingBtn = kindOneDescendants.Length > 1 ? kindOneDescendants[^1] : null;
        }
        bar.UpSprite = BtnPhaseImage(leadingBtn, "Normal");
        bar.UpRolloverSprite = BtnPhaseImage(leadingBtn, "Normal_rollover");
        bar.UpPressedSprite = BtnPhaseImage(leadingBtn, "Normal_pressed");
        bar.DownSprite = BtnPhaseImage(trailingBtn, "Normal");
        bar.DownRolloverSprite = BtnPhaseImage(trailingBtn, "Normal_rollover");
        bar.DownPressedSprite = BtnPhaseImage(trailingBtn, "Normal_pressed");
        if (details.TryFetchNetBool(0x79u, out bool concealDisabled))
            bar.HideWhenDisabled = concealDisabled;

        if (bar.Horizontal)
        {
            if (leadingBtn is { Width: > 0f })
                bar.DecrementBtnReach = leadingBtn.Width;
            if (trailingBtn is { Width: > 0f })
                bar.IncrementBtnReach = trailingBtn.Width;

            var scalarThumb = details.Children.FirstOrDefault(descendant => descendant.Id == 1u);
            bar.FollowSprite = DefaultImage(details);
            bar.ThumbSprite = scalarThumb is null ? 0u : DefaultImage(scalarThumb);

            if (bar.FollowSprite is 0u)
            {
                var authoredFollow = details.Children.FirstOrDefault(descendant => descendant.Id == 4u);
                bar.FollowSprite = authoredFollow is null ? 0u : DefaultImage(authoredFollow);
            }

            var gauge = details.Children.FirstOrDefault(descendant => descendant.Type == 7u);
            ElemDetails? populate = gauge?.Children.FirstOrDefault(descendant => descendant.Id == 2u)
                ?? gauge?.Children
                    .Where(descendant => DefaultImage(descendant) != 0u)
                    .OrderByDescending(descendant => descendant.ReadOrder)
                    .FirstOrDefault();
            bar.ScalarPopulateSprite = populate is null ? 0u : DefaultImage(populate);

            var scalarSpan = gauge?.Children.FirstOrDefault(
                descendant => descendant.Id == 0x100005EFu);
            bar.ScalarSpanSprite = scalarSpan is null
                ? 0u
                : DefaultImage(scalarSpan);
            bar.ScalarSpanArrangementRule = scalarSpan is null
                ? null
                : BuildArrangementRule(scalarSpan);
            bar.ScalarPopulateFromRight = gauge is not null
                && gauge.TryFetchNetProp(0x6Fu, out WidgetPropertyValue dir)
                && dir.Kind == WidgetPropertyKind.Enum
                && dir.UnsignedValue is 3u;
            return bar;
        }

        if (leadingBtn is { Height: > 0f })
            bar.DecrementBtnReach = leadingBtn.Height;
        if (trailingBtn is { Height: > 0f })
            bar.IncrementBtnReach = trailingBtn.Height;

        var thumb = details.Children.FirstOrDefault(descendant =>
            descendant.Type is 1u && descendant.Id != incrementIdent && descendant.Id != decrementIdent);
        if (thumb is not null)
        {
            ElemDetails[] slices = [.. thumb.Children
                .Where(descendant => DefaultImage(descendant) != 0u)
                .OrderBy(descendant => descendant.Y)
                .ThenBy(descendant => descendant.ReadOrder)];
            if (slices.Length > 0)
            {
                bar.ThumbTopSprite = BtnPhaseImage(slices[0], "Normal");
                bar.ThumbTopRolloverSprite = BtnPhaseImage(slices[0], "Normal_rollover");
                bar.ThumbTopPressedSprite = BtnPhaseImage(slices[0], "Normal_pressed");
            }
            if (slices.Length > 1)
            {
                bar.ThumbSprite = BtnPhaseImage(slices[1], "Normal");
                bar.ThumbRolloverSprite = BtnPhaseImage(slices[1], "Normal_rollover");
                bar.ThumbPressedSprite = BtnPhaseImage(slices[1], "Normal_pressed");
            }
            if (slices.Length > 2)
            {
                bar.ThumbBotSprite = BtnPhaseImage(slices[^1], "Normal");
                bar.ThumbBotRolloverSprite = BtnPhaseImage(slices[^1], "Normal_rollover");
                bar.ThumbBotPressedSprite = BtnPhaseImage(slices[^1], "Normal_pressed");
            }

            if (slices.Length is 0)
            {
                bar.ThumbSprite = BtnPhaseImage(thumb, "Normal");
                bar.ThumbRolloverSprite = BtnPhaseImage(thumb, "Normal_rollover");
                bar.ThumbPressedSprite = BtnPhaseImage(thumb, "Normal_pressed");
            }
        }

        return bar;
    }

    private static WidgetResizeGrip AssembleRescaleGrip(
        ElemDetails details, Func<uint, (uint tex, int w, int h)> locate)
    {
        bool bottom = details.TryFetchNetBool(0x2Au, out bool bottomVal) && bottomVal;
        bool left = details.TryFetchNetBool(0x2Bu, out bool leftVal) && leftVal;
        bool right = details.TryFetchNetBool(0x2Cu, out bool rightVal) && rightVal;
        bool top = details.TryFetchNetBool(0x2Du, out bool topVal) && topVal;
        return new WidgetResizeGrip(details, locate)
        {
            BorderLocale = WidgetResizeGrip.UnpackBorderLocale(bottom, left, right, top),
        };
    }

    private static WidgetGauge AssembleGauge(ElemDetails details,
        Func<uint, (uint, int, int)> locate, WidgetDatFont? datTypeface,
        Func<WidgetStringInfoValue, string?>? stringLocate = null)
    {
        WidgetGauge meter = new WidgetGauge
        {
            ElementId = details.Id,
            SpriteResolve = locate,
            DatFont = datTypeface,
            // Outline 0x21 from the meter element (round-5 ).
            Outline = details.Outline,
        };
        if (details.OutlineColor.HasValue)
            meter.OutlineColor = details.OutlineColor.Value;

        List<ElemDetails> vessels = details.Children
            .Where(c => c.Type == 3)
            .OrderBy(c => c.ReadOrder)
            .ToList();

        if (vessels.Count >= 2
            && HasThreeSliceForm(vessels[0])
            && HasThreeSliceForm(vessels[1]))
        {
            var (bl, bt, br) = SliceIdents(vessels[0]);
            meter.BackLeft = bl;
            meter.BackTile = bt;
            meter.BackRight = br;

            var (fl, ft, fr) = SliceIdents(vessels[1]);
            meter.FrontLeft = fl;
            meter.FrontTile = ft;
            meter.FrontRight = fr;

            var backTopLayer = SpecificsTopLayer(vessels[0]);
            var frontTopLayer = SpecificsTopLayer(vessels[1]);
            if (backTopLayer is not null || frontTopLayer is not null)
            {
                bool passToDescendants = details.States.Values.Any(
                    static s => s.Name is "HideDetail" or "ShowDetail" && s.PassToChildren);
                meter.ConfigureSpecificsTopLayer(
                    SpecificsTopLayerSpec(backTopLayer, vessels[0]),
                    SpecificsTopLayerSpec(frontTopLayer, vessels[1]),
                    passToDescendants);
            }
        }
        else if (vessels.Count is 1
            && vessels[0].X == 0f && vessels[0].Y == 0f
            && vessels[0].Width == details.Width && vessels[0].Height == details.Height
            && details.States.TryGetValue(WidgetStateInfo.StraightPhaseIdent, out var backPhase)
            && backPhase.LoopingAnimation is { DrawMode: 1 } backAnim
            && vessels[0].States.TryGetValue(WidgetStateInfo.StraightPhaseIdent, out var frontPhase)
            && frontPhase.LoopingAnimation is { DrawMode: 1 } frontAnim
            && backAnim.Duration == frontAnim.Duration)
        {
            meter.ConfigureMovingTracks(backAnim, frontAnim);
        }
        else if (vessels.Count is 1 && vessels[0].StateMedia.ContainsKey(""))
        {
            meter.BackLeft = 0;
            meter.BackTile = details.StateMedia.TryGetValue("", out var bm) ? bm.File : 0u;
            meter.BackRight = 0;

            meter.FrontLeft = 0;
            meter.FrontTile = vessels[0].StateMedia.TryGetValue("", out var fm) ? fm.File : 0u;
            meter.FrontRight = 0;
        }
        else if (vessels.Any(HasStatefulPopulate))
        {
            meter.BackLeft = 0;
            meter.BackTile = details.StateMedia.TryGetValue("", out var follow) ? follow.File : 0u;
            meter.BackRight = 0;
            meter.FrontLeft = 0;
            meter.FrontTile = 0;
            meter.FrontRight = 0;

            foreach (ElemDetails vessel in vessels)
            {
                foreach (var (phaseIdent, phase) in vessel.States)
                {
                    if (phaseIdent == WidgetStateInfo.StraightPhaseIdent
                        || !vessel.StateMedia.TryGetValue(phase.Name, out var media))
                        continue;
                    meter.ConfigurePhasePopulate(phaseIdent, media.File);
                }
            }

            foreach (ElemDetails phraseDescendant in details.Children.Where(static c => c.Type == 12))
            {
                foreach (var (phaseIdent, phase) in phraseDescendant.States)
                {
                    if (phaseIdent == WidgetStateInfo.StraightPhaseIdent
                        || !phase.Properties.Values.TryGetValue(0x17u, out var legend)
                        || legend.Kind != WidgetPropertyKind.StringInfo)
                        continue;
                    if (stringLocate?.Invoke(legend.StringInfoValue) is not { Length: > 0 } phrase)
                        continue;
                    WidgetMeterLabelAlign align = phraseDescendant.HJustify switch
                    {
                        ClientHJustify.Left => WidgetMeterLabelAlign.Left,
                        ClientHJustify.Right => WidgetMeterLabelAlign.Right,
                        _ => WidgetMeterLabelAlign.Center,
                    };
                    if (phase.Properties.Values.TryGetValue(0x14u, out var justify)
                        && justify.Kind == WidgetPropertyKind.Enum)
                    {
                        align = ElemScanner.ChartHorizontalJustification(
                            justify.UnsignedValue) switch
                        {
                            ClientHJustify.Left => WidgetMeterLabelAlign.Left,
                            ClientHJustify.Right => WidgetMeterLabelAlign.Right,
                            _ => WidgetMeterLabelAlign.Center,
                        };
                    }
                    meter.ConfigurePhaseCaption(phaseIdent, phrase, align);
                }
            }
        }
        else
        {
            Console.WriteLine($"[UI] meter 0x{details.Id:X8}: {vessels.Count} Type-3 containers but no recognized 3-slice, direct-fill, or stateful-fill shape - bar may render as solid-color fallback");
        }

        return meter;
    }

    private static WidgetElem AssemblePhrase(ElemDetails details, Func<uint, (uint, int, int)> locate,
        WidgetDatFont? elemTypeface = null,
        Func<WidgetStringInfoValue, string?>? stringLocate = null)
    {
        uint bg = details.StateMedia.TryGetValue(
                      !string.IsNullOrEmpty(details.DefaultStateName) ? details.DefaultStateName
                    : details.StateMedia.ContainsKey("Normal") ? "Normal" : "", out var m)
                  ? m.File : 0u;

        bool editable = details.TryFetchNetBool(0x16u, out var editableVal)
            && editableVal;
        bool selectable = details.TryFetchNetBool(0x27u, out var selectableVal)
            && selectableVal;
        bool oneStroke = details.TryFetchNetBool(0x20u, out var oneStrokeVal)
            && oneStrokeVal;

        if (editable)
        {
            uint focusSprite = details.StateMedia.TryGetValue("Normal_focussed", out var focus)
                ? focus.File
                : 0u;
            WidgetField field = new WidgetField
            {
                ElementId = details.Id,
                DatTypeface = elemTypeface,
                SpriteLocate = locate,
                BackgroundSprite = bg,
                FocusFieldSprite = focusSprite,
                Selectable = selectable,
                OneStroke = oneStroke,
                Centered = details.HJustify == ClientHJustify.Center,
                RightAligned = details.HJustify == ClientHJustify.Right,
                // Outline 0x21 from the field element (round-5 )
                Outline = details.Outline,
            };
            if (details.TryFetchNetInteger(0x1Eu, out int upperToons))
                field.UpperToons = upperToons;
            if (details.FontColor.HasValue)
                field.PhraseTint = details.FontColor.Value;
            if (details.OutlineColor.HasValue)
                field.OutlineColor = details.OutlineColor.Value;

            foreach (ElemDetails descendant in details.Children)
            {
                if (descendant.Type is not 3u
                    || !descendant.StateMedia.TryGetValue("Normal_focussed", out var railMedia)
                    || railMedia.File is 0u)
                    continue;
                bool leftAnchored = descendant.X < details.Width * 0.5f;
                if (leftAnchored)
                {
                    field.FocusRailLeftSprite = railMedia.File;
                    if (descendant.Width > 0f) field.FocusRailLeftWidth = descendant.Width;
                }
                else
                {
                    field.FocusRailRightSprite = railMedia.File;
                    if (descendant.Width > 0f) field.FocusRailRightWidth = descendant.Width;
                }
            }
            return field;
        }

        bool centered = details.HJustify == ClientHJustify.Center;
        bool rightAligned = details.HJustify == ClientHJustify.Right;
        ClientVJustify vJustify = details.VJustify;

        WidgetPhrase t = new WidgetPhrase
        {
            ElementId = details.Id,
            BackgroundSprite = bg,
            SpriteResolve = locate,
            Centered = centered,
            RightAligned = rightAligned,
            VerticalJustify = vJustify,
            OneLine = oneStroke,
            Selectable = selectable,
            DatFont = elemTypeface,
            TypefaceTintSwatch = ElemScanner.ScanNetTintSwatch(
                details,
                0x1Bu),
            Outline = details.Outline,
            MarginLeft = details.MarginLeft,
            MarginRight = details.MarginRight,
            MarginTop = details.MarginTop,
            MarginBottom = details.MarginBottom,
        };
        t.ConfigureDatPhase(details);

        if (details.FontColor.HasValue)
            t.DefaultTint = details.FontColor.Value;
        if (details.TagFontColor.HasValue)
            t.TagTint = details.TagFontColor.Value;

        if (details.OutlineColor.HasValue)
            t.OutlineColor = details.OutlineColor.Value;

        if (LocateAuthoredString(details, stringLocate) is { Length: > 0 } authored)
        {
            if (authored.Contains('\n'))
            {
                float stashedWidth = float.NaN;
                WidgetDatFont? stashedTypeface = null;
                System.Numerics.Vector4 stashedTint = default;
                WidgetPhrase.Line[]? stashedStrokes = null;
                t.StrokesSupplier = () =>
                {
                    if (stashedStrokes is null
                        || stashedWidth != t.Width
                        || !ReferenceEquals(stashedTypeface, t.DatFont)
                        || stashedTint != t.DefaultTint)
                    {
                        stashedWidth = t.Width;
                        stashedTypeface = t.DatFont;
                        stashedTint = t.DefaultTint;
                        float ceilingWidth = Math.Max(
                            1f,
                            t.Width - (t.Padding + t.MarginLeft) - (t.Padding + t.MarginRight));
                        Func<string, float> gauge = t.DatFont is { } typeface
                            ? typeface.MeasureWidth
                            : static val => val.Length * 8f;
                        stashedStrokes = [.. WidgetPhrase
                            .EncloseWords(authored, gauge, ceilingWidth)
                            .Select(stroke => new WidgetPhrase.Line(stroke, t.DefaultTint))];
                    }
                    return stashedStrokes;
                };
            }
            else
            {
                t.StrokesSupplier = () =>
                    [new WidgetPhrase.Line(authored, t.DefaultTint)];
            }
        }

        Dictionary<uint, string>? phaseTexts = null;
        foreach (var (phaseIdent, phase) in details.States)
        {
            if (phaseIdent == WidgetStateInfo.StraightPhaseIdent
                || !phase.Properties.Values.TryGetValue(0x17u, out var phaseLegend)
                || phaseLegend.Kind != WidgetPropertyKind.StringInfo)
                continue;
            if (stringLocate?.Invoke(phaseLegend.StringInfoValue)
                is { Length: > 0 } phrase)
                (phaseTexts ??= [])[phaseIdent] = phrase;
        }
        if (phaseTexts is not null)
            t.AssignAuthoredPhaseTexts(phaseTexts);

        return t;
    }

    private static WidgetBtn AssembleBtn(
        ElemDetails details,
        Func<uint, (uint, int, int)> locate,
        WidgetDatFont? elemTypeface,
        Func<uint, WidgetDatFont?>? typefaceLocate,
        Func<WidgetStringInfoValue, string?>? stringLocate)
    {
        ElemDetails[] authoredFaces = details.StateMedia.Count is 0
            ? SeekStatefulFaceDescendants(details)
            : [];
        ElemDetails? face = authoredFaces.Length is 1 ? authoredFaces[0] : null;
        IReadOnlyList<ElemDetails>? faceSegments = authoredFaces.Length > 1
            ? authoredFaces
            : null;

        string? caption = LocateAuthoredString(details, stringLocate);
        ElemDetails captionDetails = details;
        if (caption is null)
        {
            foreach (ElemDetails descendant in details.Children.Where(child => child.Type == 12u))
            {
                caption = LocateAuthoredString(descendant, stringLocate);
                if (caption is null) continue;
                captionDetails = descendant;
                break;
            }
        }

        var captionTypeface = elemTypeface;
        if (captionDetails.FontDid is not 0u && typefaceLocate is not null)
            captionTypeface = typefaceLocate(captionDetails.FontDid) ?? elemTypeface;

        WidgetBtn btn = new WidgetBtn(details, locate, face, faceSegments)
        {
            Label = caption,
            LabelFont = captionTypeface,
            CaptionColor = captionDetails.FontColor ?? details.FontColor
                ?? System.Numerics.Vector4.One,
            Outline = captionDetails.Outline || details.Outline,
        };
        if ((captionDetails.OutlineColor ?? details.OutlineColor) is { } btnOutlineTint)
            btn.OutlineColor = btnOutlineTint;

        if (face is not null)
        {
            AssembleBtnBranch2(btn, face, captionDetails, details);
        }
        else if (captionDetails.HJustify == ClientHJustify.Left)
        {
            btn.CaptionAlign = WidgetBtn.CaptionAlignment.Left;
            if (!ReferenceEquals(captionDetails, details))
                btn.CaptionShiftX = captionDetails.X;
        }

        btn.AssignPerPhaseCaptionStyling(
            ElemScanner.AssemblePerPhaseTintLookup(captionDetails, 0x1Bu),
            ElemScanner.AssemblePerPhaseBoolLookup(captionDetails, 0x21u));

        if (ReferenceEquals(captionDetails, details) && caption is not null)
        {
            var valDescendant = details.Children.FirstOrDefault(
                child => child.Type is 12u && child.StateMedia.Count is 0);
            if (valDescendant is not null)
            {
                AssembleBtnBranch(btn, valDescendant, details, typefaceLocate, elemTypeface, stringLocate);
            }
        }

        return btn;
    }

    private static void AssembleBtnBranch(WidgetBtn btn, ElemDetails valDescendant, ElemDetails details, Func<uint, WidgetDatFont?>? typefaceLocate, WidgetDatFont? elemTypeface, Func<WidgetStringInfoValue, string?>? stringLocate)
    {
        btn.ValBbox = ReflowValDescendantRect(valDescendant, details);
        btn.ValTypeface = valDescendant.FontDid is not 0u && typefaceLocate is not null
                            ? typefaceLocate(valDescendant.FontDid) ?? elemTypeface
                            : elemTypeface;
        btn.ValTint = valDescendant.FontColor ?? System.Numerics.Vector4.One;
        btn.ValAlign = valDescendant.HJustify switch
        {
            ClientHJustify.Left => WidgetBtn.CaptionAlignment.Left,
            ClientHJustify.Right => WidgetBtn.CaptionAlignment.Right,
            _ => WidgetBtn.CaptionAlignment.Center,
        };
        btn.ValCaption = LocateAuthoredString(valDescendant, stringLocate);
    }

    private static void AssembleBtnBranch2(WidgetBtn btn, ElemDetails face, ElemDetails captionDetails, ElemDetails details)
    {
        btn.FaceLeft = face.X;
        btn.FaceTop = face.Y;
        btn.FaceWidth = face.Width;
        btn.FaceHeight = face.Height;
        if (ReferenceEquals(captionDetails, details))
        {
            btn.CaptionAlign = WidgetBtn.CaptionAlignment.Left;
            btn.CaptionShiftX = face.X + face.Width + 4f;
        }
        else
        {
            btn.LabelBox = (captionDetails.X, captionDetails.Y, captionDetails.Width, captionDetails.Height);
            btn.CaptionAlign = captionDetails.HJustify == ClientHJustify.Left
                ? WidgetBtn.CaptionAlignment.Left
                : WidgetBtn.CaptionAlignment.Center;
        }
    }

    private static WidgetBtn AssembleTickbox(
        ElemDetails details,
        Func<uint, (uint, int, int)> locate,
        WidgetDatFont? elemTypeface,
        Func<uint, WidgetDatFont?>? typefaceLocate,
        Func<WidgetStringInfoValue, string?>? stringLocate)
    {
        var indicator = SeekStatefulFaceDescendant(details);
        WidgetBtn btn = new WidgetBtn(details, locate, indicator)
        {
            Label = LocateAuthoredString(details, stringLocate),
            LabelFont = details.FontDid is not 0u && typefaceLocate is not null
                ? typefaceLocate(details.FontDid) ?? elemTypeface
                : elemTypeface,
            CaptionColor = details.FontColor ?? System.Numerics.Vector4.One,
            CaptionAlign = WidgetBtn.CaptionAlignment.Left,
            Outline = details.Outline,
        };
        if (details.OutlineColor.HasValue)
            btn.OutlineColor = details.OutlineColor.Value;

        if (indicator is not null)
        {
            btn.FaceLeft = indicator.X;
            btn.FaceTop = indicator.Y;
            btn.FaceWidth = indicator.Width;
            btn.FaceHeight = indicator.Height;
            btn.CaptionShiftX = indicator.X + indicator.Width + 4f;
        }

        return btn;
    }
}
