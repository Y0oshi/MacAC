namespace MacAC.Client.Graphics;

/// <summary>
/// What one world frame drew, and the handful of facts that decide which steps run. The painter
/// fills this in once per frame and hands it to the step table; it is reused, so a frame costs no
/// allocation.
/// </summary>
internal sealed class RealmFrameContext
{
    /// <summary>The player is inside an interior, so the interior pass owns the view.</summary>
    public bool Interior { get; set; }

    /// <summary>The sky is in view this frame.</summary>
    public bool Sky { get; set; }

    /// <summary>Set by whichever step painted the sky, so weather knows it has a sky to sit on.</summary>
    public bool SkyPainted { get; set; }

    public Action ClipPlane { get; set; } = Nothing;
    public Action PaintSky { get; set; } = Nothing;
    public Action PaintLand { get; set; } = Nothing;
    public Action PaintInterior { get; set; } = Nothing;
    public Action PaintActors { get; set; } = Nothing;
    public Action CloseClipGaps { get; set; } = Nothing;
    public Action PaintMotes { get; set; } = Nothing;
    public Action PaintWeather { get; set; } = Nothing;

    private static readonly Action Nothing = static () => { };

    public void Reset()
    {
        Interior = false;
        Sky = false;
        SkyPainted = false;
    }
}

/// <summary>One drawing step of a world frame: when it runs, and what it does.</summary>
internal readonly record struct RealmFrameStep(
    string Name,
    Func<RealmFrameContext, bool> When,
    Action<RealmFrameContext> Run);

/// <summary>
/// The order a world frame draws in. The array is the order; each step decides for itself whether
/// it applies this frame, so the sequence can be asserted without a GPU or a live world.
/// </summary>
internal static class RealmFrameSteps
{
    /// <summary>
    /// Outdoors: establish the ground plane, then sky, land and actors, then close the interior
    /// gaps, then the particles that sit on top, then weather over the sky that was painted.
    /// Indoors the interior pass replaces the first four.
    /// </summary>
    public static readonly RealmFrameStep[] InOrder =
    [
        new("clip-plane", static ctx => !ctx.Interior, static ctx => ctx.ClipPlane()),
        new("sky", static ctx => !ctx.Interior && ctx.Sky, static ctx =>
        {
            ctx.PaintSky();
            ctx.SkyPainted = true;
        }),
        new("land", static ctx => !ctx.Interior, static ctx => ctx.PaintLand()),
        new("interior", static ctx => ctx.Interior, static ctx => ctx.PaintInterior()),
        new("actors", static ctx => !ctx.Interior, static ctx => ctx.PaintActors()),
        new("clip-gaps", static _ => true, static ctx => ctx.CloseClipGaps()),
        new("motes", static _ => true, static ctx => ctx.PaintMotes()),
        new("weather", static ctx => !ctx.Interior && ctx.SkyPainted, static ctx => ctx.PaintWeather()),
    ];

    /// <summary>The steps that apply to a frame, in order. Used by the painter and by its tests.</summary>
    public static IEnumerable<string> PlanFor(RealmFrameContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        bool skyPainted = context.SkyPainted;
        try
        {
            foreach (var step in InOrder)
            {
                if (!step.When(context)) continue;
                if (step.Name == "sky") context.SkyPainted = true;
                yield return step.Name;
            }
        }
        finally
        {
            context.SkyPainted = skyPainted;
        }
    }

    public static void Run(RealmFrameContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (var step in InOrder)
        {
            if (step.When(context))
                step.Run(context);
        }
    }
}
