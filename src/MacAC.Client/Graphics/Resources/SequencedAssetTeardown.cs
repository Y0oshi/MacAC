namespace MacAC.Client.Graphics;

internal sealed class SequencedAssetTeardown(params Action[] stages)
{
    private readonly Action[] _junctures = stages ?? throw new ArgumentNullException(nameof(stages));
    private bool _advancing;

    public bool IsComplete => UpcomingJuncture == _junctures.Length;
    internal int UpcomingJuncture { get; private set; }

    public void Advance()
    {
        if (_advancing || IsComplete)
            return;

        _advancing = true;
        try
        {
            while (UpcomingJuncture < _junctures.Length)
            {
                _junctures[UpcomingJuncture]();
                ++UpcomingJuncture;
            }
        }
        finally
        {
            _advancing = false;
        }
    }
}
