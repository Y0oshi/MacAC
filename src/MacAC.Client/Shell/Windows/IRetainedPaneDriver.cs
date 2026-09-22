namespace MacAC.Client.Shell;

public interface IRetainedPaneDriver : IDisposable
{
    void OnShown() { }

    void OnConcealed() { }

    void OnDescendantFocusAltered(WidgetElem? focusedDescendant) { }
}
