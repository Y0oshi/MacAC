namespace MacAC.Client.Shell.Panels;

public enum CanonWindowChrome
{
    Imported,

    NineSlice,

    CollapsibleNineSlice,
}

public static class CanonPaneCycle
{
    public sealed record Options
    {
        public required string PaneMoniker { get; init; }
        public CanonWindowChrome Chrome { get; init; } = CanonWindowChrome.NineSlice;

        public float Left { get; init; }
        public float Top { get; init; }

        public float? SubstanceWidth { get; init; }
        public float? SubstanceHeight { get; init; }
        public bool RebaseSubstanceArrangement { get; init; }

        public ElemDetails? DatConstraintSrc { get; init; }

        public bool DatConstraintSrcIsOuterCycle { get; init; }

        public float? MinWidth { get; init; }
        public float? MinHeight { get; init; }
        public float? MaxWidth { get; init; }
        public float? MaxHeight { get; init; }

        public bool Draggable { get; init; } = true;
        public bool Resizable { get; init; } = true;
        public bool RescaleX { get; init; } = true;
        public bool RescaleY { get; init; } = true;
        public RescaleRims ResizableRims { get; init; } =
            RescaleRims.Left | RescaleRims.Right | RescaleRims.Top | RescaleRims.Bottom;
        public bool ConstrainPullToAncestor { get; init; }
        public bool ConstrainRescaleToAncestor { get; init; }

        public float Opacity { get; init; } = 1f;
        public bool PaintChromeMiddle { get; init; } = true;
        public bool Visible { get; init; } = true;

        public int AuthoredGeoRevision { get; init; }

        public MooringRims OuterMoorings { get; init; } = MooringRims.None;

        public MooringRims SubstanceMoorings { get; init; } =
            MooringRims.Left | MooringRims.Top | MooringRims.Right | MooringRims.Bottom;
        public bool? SubstancePressThrough { get; init; }
        public IRetainedPaneDriver? Controller { get; init; }
        public IRetainedWindowStateDriver? ConditionDriver { get; init; }
    }

    public static CanonWindowHandle Mount(
        WidgetTrunk trunk,
        WidgetElem substance,
        Func<uint, (uint handle, int w, int h)> locateChrome,
        Options options)
    {
        ArgumentNullException.ThrowIfNull(trunk);
        ArgumentNullException.ThrowIfNull(substance);
        ArgumentNullException.ThrowIfNull(locateChrome);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.PaneMoniker);

        float substanceWidth = options.SubstanceWidth ?? substance.Width;
        float substanceHeight = options.SubstanceHeight ?? substance.Height;
        bool wrapped = options.Chrome != CanonWindowChrome.Imported;
        int inset = wrapped ? 2 * CanonChromeSprites.Border : 0;
        float outerWidth = substanceWidth + inset;
        float outerHeight = substanceHeight + inset;

        WidgetElem outerCycle;
        if (wrapped)
        {
            outerCycle = options.Chrome switch
            {
                CanonWindowChrome.NineSlice => new WidgetNineSlicePane(locateChrome),
                CanonWindowChrome.CollapsibleNineSlice => new WidgetCollapsibleFrame(locateChrome),
                _ => throw new ArgumentOutOfRangeException(nameof(options.Chrome)),
            };

            if (outerCycle is WidgetNineSlicePane nineSlice)
                nineSlice.PaintMiddlePopulate = options.PaintChromeMiddle;

            const int border = CanonChromeSprites.Border;
            substance.Left = border;
            substance.Top = border;
            substance.Width = substanceWidth;
            substance.Height = substanceHeight;
            substance.Moorings = options.SubstanceMoorings;
            substance.Draggable = false;
            substance.Resizable = false;
            if (options.SubstancePressThrough is { } pressThrough)
                substance.ClickThrough = pressThrough;
            if (options.RebaseSubstanceArrangement)
                substance.RebaseDescendantArrangementBaselines();
            outerCycle.AddChild(substance);
        }
        else
        {
            outerCycle = substance;
            substance.Width = substanceWidth;
            substance.Height = substanceHeight;
            substance.Moorings = MooringRims.None;
            if (options.SubstancePressThrough is { } pressThrough)
                substance.ClickThrough = pressThrough;
            if (options.RebaseSubstanceArrangement)
                substance.RebaseDescendantArrangementBaselines();
        }

        outerCycle.Left = options.Left;
        outerCycle.Top = options.Top;
        outerCycle.Width = outerWidth;
        outerCycle.Height = outerHeight;
        outerCycle.Moorings = options.OuterMoorings;
        outerCycle.Draggable = options.Draggable;
        outerCycle.Resizable = options.Resizable;
        outerCycle.ResizeX = options.RescaleX;
        outerCycle.ResizeY = options.RescaleY;
        outerCycle.ResizableEdges = options.ResizableRims;
        outerCycle.ConstrainPullToParent = options.ConstrainPullToAncestor;
        outerCycle.ConstrainRescaleToParent = options.ConstrainRescaleToAncestor;
        outerCycle.Opacity = Math.Clamp(options.Opacity, 0f, 1f);
        outerCycle.Visible = options.Visible;

        int constraintInset = options.DatConstraintSrcIsOuterCycle ? 0 : inset;
        outerCycle.MinWidth = LocateConstraint(
            options.MinWidth, options.DatConstraintSrc, 0x3Fu, outerWidth, constraintInset);
        outerCycle.MinHeight = LocateConstraint(
            options.MinHeight, options.DatConstraintSrc, 0x3Eu, outerHeight, constraintInset);
        outerCycle.MaxWidth = Math.Max(
            outerCycle.MinWidth,
            LocateConstraint(options.MaxWidth, options.DatConstraintSrc, 0x3Du, float.MaxValue, constraintInset));
        outerCycle.MaxHeight = Math.Max(
            outerCycle.MinHeight,
            LocateConstraint(options.MaxHeight, options.DatConstraintSrc, 0x3Cu, float.MaxValue, constraintInset));

        if (outerWidth < outerCycle.MinWidth || outerWidth > outerCycle.MaxWidth)
            throw new InvalidOperationException(
                $"RetailWindowFrame.Mount(\"{options.PaneMoniker}\"): mounted outer width " +
                $"{outerWidth} is beyond its own clamp [{outerCycle.MinWidth}, {outerCycle.MaxWidth}] " +
                "- the window would open by now violating its authored resize bounds");
        if (outerHeight < outerCycle.MinHeight || outerHeight > outerCycle.MaxHeight)
            throw new InvalidOperationException(
                $"RetailWindowFrame.Mount(\"{options.PaneMoniker}\"): mounted outer height " +
                $"{outerHeight} is beyond its own clamp [{outerCycle.MinHeight}, {outerCycle.MaxHeight}] " +
                "- the window would open by now violating its authored resize bounds");

        if (wrapped)
            substance.ImposeMooring(outerWidth, outerHeight);

        trunk.AddChild(outerCycle);
        return trunk.ListPane(
            options.PaneMoniker,
            outerCycle,
            substance,
            options.Controller,
            options.ConditionDriver,
            options.AuthoredGeoRevision);
    }

    private static float LocateConstraint(
        float? explicitVal,
        ElemDetails? datSrc,
        uint propIdent,
        float backup,
        int chromeInset)
    {
        if (explicitVal is { } val)
            return val;
        return datSrc is not null
            && datSrc.TryFetchNetInteger(propIdent, out int datVal)
            && datVal >= 0
            ? datVal + chromeInset
            : backup;
    }
}
