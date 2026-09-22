namespace MacAC.Client.Graphics.Packs;

internal interface IRenderPackPreparationRota
{
    Task Book(Action prep);
}

internal sealed class ThreadPoolRenderPackPreparationRota :
    IRenderPackPreparationRota
{
    internal static ThreadPoolRenderPackPreparationRota Instance { get; } = new();

    private ThreadPoolRenderPackPreparationRota()
    {
    }

    public Task Book(Action prep)
    {
        ArgumentNullException.ThrowIfNull(prep);
        return Task.Run(prep);
    }
}

internal sealed class InlineRenderPackPreparationRota :
    IRenderPackPreparationRota
{
    internal static InlineRenderPackPreparationRota Instance { get; } = new();

    private InlineRenderPackPreparationRota()
    {
    }

    public Task Book(Action prep)
    {
        ArgumentNullException.ThrowIfNull(prep);
        prep();
        return Task.CompletedTask;
    }
}
