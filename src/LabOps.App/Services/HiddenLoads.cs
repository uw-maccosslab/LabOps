namespace LabOps.App.Services;

/// <summary>
/// Whether a view's page was loaded while the view was hidden. The browser never draws a page it
/// loaded hidden, so the view would show a black box until its next page; instead the page is
/// loaded again as the view appears (<see cref="WebViewGuard.RedrawWhenShown"/>).
/// </summary>
/// <param name="isVisible">Whether the view shows now.</param>
/// <param name="load">Loads the view's current page, calling <see cref="Loading"/> as it does.</param>
internal sealed class HiddenLoads(Func<bool> isVisible, Action load)
{
    private bool _pending;

    /// <summary>A page is being loaded into the view.</summary>
    public void Loading() => _pending = !isVisible();

    /// <summary>The view appeared: its page is loaded again if it was loaded while hidden.</summary>
    public void Shown()
    {
        if (_pending)
        {
            _pending = false;
            load();
        }
    }
}
