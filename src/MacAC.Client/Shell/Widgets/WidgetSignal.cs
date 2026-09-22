namespace MacAC.Client.Shell;

public readonly record struct WidgetSignal(
    uint SourceId,
    WidgetElem? Target,
    int Type,
    int Data0 = 0,
    int Data1 = 0,
    int Data2 = 0,
    int Data3 = 0,
    object? Payload = null);

public static class WidgetEventType
{
    public const int Click = 0x01;
    public const int HoverJoin = 0x05;
    public const int HoverDepart = 0x06;
    public const int Tooltip = 0x07;
    public const int DoublePress = 0x08;
    public const int Roll = 0x0A;
    public const int RightPress = 0x0E;
    public const int PullCommence = 0x15;
    public const int PullOver = 0x1C;
    public const int PullJoin = 0x21;
    public const int FocusLost = 0x28;
    public const int FocusGained = 0x29;
    public const int DiscardReleased = 0x3E;

    public const int PointerRelocate = 0x200;
    public const int PointerDown = 0x201;   // left button down
    public const int PointerUp = 0x202;   // left button up
    public const int GrabAltered = 0x215;
    public const int DoublePressLeft = 0x203;
    public const int RightDown = 0x204;
    public const int RightUp = 0x205;
    public const int MiddleDown = 0x207;
    public const int MiddleUp = 0x208;

    public const int TagDown = 0x100;
    public const int TagUp = 0x101;
    public const int Char = 0x102;
}

public enum WidgetMouseButton
{
    Left = 1,
    Right = 2,
    Middle = 3,
}
