namespace MacAC.Client.Shell;

public interface IWidgetViewportPainter
{
    uint Render(int width, int height);

    bool TextureIsBottomUp { get; }
}
