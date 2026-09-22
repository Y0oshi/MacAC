namespace MacAC.Client.Shell;

internal static class CanonScrollbarChrome
{
    internal const uint Follow = 0x06004C5Fu;

    internal const uint UpNorm = 0x06004C6Cu;
    internal const uint UpRollover = 0x06004C6Du;
    internal const uint UpPressed = 0x06004C6Eu;

    internal const uint DownNorm = 0x06004C69u;
    internal const uint DownRollover = 0x06004C6Au;
    internal const uint DownPressed = 0x06004C6Bu;

    internal const uint ThumbTopNorm = 0x06004C60u;
    internal const uint ThumbTopRollover = 0x06004C61u;
    internal const uint ThumbTopPressed = 0x06004C62u;
    internal const uint ThumbMidNorm = 0x06004C63u;
    internal const uint ThumbMidRollover = 0x06004C64u;
    internal const uint ThumbMidPressed = 0x06004C65u;
    internal const uint ThumbBotNorm = 0x06004C66u;
    internal const uint ThumbBotRollover = 0x06004C67u;
    internal const uint ThumbBotPressed = 0x06004C68u;

    internal const uint HFollow = 0x06004C7Fu;

    internal const uint LeftNorm = 0x06004C8Cu;
    internal const uint LeftRollover = 0x06004C8Du;
    internal const uint LeftPressed = 0x06004C8Eu;

    internal const uint RightNorm = 0x06004C89u;
    internal const uint RightRollover = 0x06004C8Au;
    internal const uint RightPressed = 0x06004C8Bu;

    internal const uint HThumbTopNorm = 0x06004C80u;
    internal const uint HThumbTopRollover = 0x06004C81u;
    internal const uint HThumbTopPressed = 0x06004C82u;
    internal const uint HThumbMidNorm = 0x06004C83u;
    internal const uint HThumbMidRollover = 0x06004C84u;
    internal const uint HThumbMidPressed = 0x06004C85u;
    internal const uint HThumbBotNorm = 0x06004C86u;
    internal const uint HThumbBotRollover = 0x06004C87u;
    internal const uint HThumbBotPressed = 0x06004C88u;

    internal static void ImposeVertical(WidgetScroller bar)
    {
        bar.FollowSprite = Follow;
        bar.UpSprite = UpNorm;
        bar.UpRolloverSprite = UpRollover;
        ImposeVerticalRest(bar);
    }

    private static void ImposeVerticalRest(WidgetScroller bar)
    {
        bar.UpPressedSprite = UpPressed;
        bar.DownSprite = DownNorm;
        ImposeVerticalTail(bar);
    }

    private static void ImposeVerticalTail(WidgetScroller bar)
    {
        bar.DownRolloverSprite = DownRollover;
        bar.DownPressedSprite = DownPressed;
        bar.ThumbTopSprite = ThumbTopNorm;
        ImposeVerticalCoda(bar);
    }

    private static void ImposeVerticalCoda(WidgetScroller bar)
    {
        bar.ThumbTopRolloverSprite = ThumbTopRollover;
        bar.ThumbTopPressedSprite = ThumbTopPressed;
        bar.ThumbSprite = ThumbMidNorm;
        bar.ThumbRolloverSprite = ThumbMidRollover;
        ImposeVerticalCoda2(bar);
    }

    private static void ImposeVerticalCoda2(WidgetScroller bar)
    {
        bar.ThumbPressedSprite = ThumbMidPressed;
        bar.ThumbBotSprite = ThumbBotNorm;
        bar.ThumbBotRolloverSprite = ThumbBotRollover;
        bar.ThumbBotPressedSprite = ThumbBotPressed;
    }

    internal static void ImposeToMenuPopup(WidgetMenu menu)
    {
        menu.RollFollowSprite = Follow;
        menu.RollThumbTopSprite = ThumbTopNorm;
        menu.RollThumbSprite = ThumbMidNorm;
        ImposeToMenuPopupRest(menu);
    }

    private static void ImposeToMenuPopupRest(WidgetMenu menu)
    {
        menu.RollThumbBottomSprite = ThumbBotNorm;
        menu.RollUpSprite = UpNorm;
        menu.RollDownSprite = DownNorm;
    }

    internal static void ImposeHorizontal(WidgetScroller bar)
    {
        bar.FollowSprite = HFollow;
        bar.UpSprite = LeftNorm;
        bar.UpRolloverSprite = LeftRollover;
        ImposeHorizontalRest(bar);
    }

    private static void ImposeHorizontalRest(WidgetScroller bar)
    {
        bar.UpPressedSprite = LeftPressed;
        bar.DownSprite = RightNorm;
        ImposeHorizontalTail(bar);
    }

    private static void ImposeHorizontalTail(WidgetScroller bar)
    {
        bar.DownRolloverSprite = RightRollover;
        bar.DownPressedSprite = RightPressed;
        bar.ThumbTopSprite = HThumbTopNorm;
        ImposeHorizontalCoda(bar);
    }

    private static void ImposeHorizontalCoda(WidgetScroller bar)
    {
        bar.ThumbTopRolloverSprite = HThumbTopRollover;
        bar.ThumbTopPressedSprite = HThumbTopPressed;
        bar.ThumbSprite = HThumbMidNorm;
        bar.ThumbRolloverSprite = HThumbMidRollover;
        ImposeHorizontalCoda2(bar);
    }

    private static void ImposeHorizontalCoda2(WidgetScroller bar)
    {
        bar.ThumbPressedSprite = HThumbMidPressed;
        bar.ThumbBotSprite = HThumbBotNorm;
        bar.ThumbBotRolloverSprite = HThumbBotRollover;
        bar.ThumbBotPressedSprite = HThumbBotPressed;
    }
}
