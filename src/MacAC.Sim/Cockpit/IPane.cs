namespace MacAC.Cockpit;

/// <summary>A drawable UI window with a stable id for layout persistence.</summary>
public interface IPane
{
    string Id { get; }

    string Title { get; }

    bool IsVisible { get; set; }

    void Render(PaneContext cx, IPaneRenderer painter);
}
