using MacAC.Client.Realm;

namespace MacAC.Client.Paging;

internal sealed class PagingOriginRecenterMarshal(
    PagingDriver streaming,
    OnlineRealmOriginLedger origin) : IPagingOriginConvergence
{
    private readonly record struct RequestDef(
        int DestinationX,
        int DestinationY,
        bool IsSealedDungeon,
        bool CancelAtSessionBoundary = false);

    private readonly PagingDriver _paging = streaming ?? throw new ArgumentNullException(nameof(streaming));
    private readonly OnlineRealmOriginLedger _origin = origin ?? throw new ArgumentNullException(nameof(origin));
    private RequestDef? _queued;
    private RequestDef? _substitute;
    private bool _originSealed;
    private bool _admitSubstitute;
    private bool _srcWasSealedDungeon;
    private bool _advancing;

    public bool IsPending => _queued is not null;

    public bool Begin(int destX, int destY, bool isSealedDungeon)
    {
        RequestDef req = new RequestDef(destX, destY, isSealedDungeon);
        if (_queued is { } queued && queued != req)
        {
            if (!_admitSubstitute)
            {
                throw new InvalidOperationException(
                    "A different streaming-origin recenter is by now pending");
            }

            _admitSubstitute = false;
            if (_originSealed)
                _substitute = req;
            else
                _queued = req;
        }

        if (_queued is null)
        {
            _queued = req;
            _admitSubstitute = false;
            _srcWasSealedDungeon = _paging.IsCollapsedToDungeon;
            _paging.BeginOriginRecenter();
        }

        return Advance();
    }

    public bool Advance()
    {
        if (_advancing)
            return false;

        _advancing = true;
        try
        {
            while (_queued is { } req)
            {
                if (!_originSealed)
                {
                    if (!_paging.IsOriginRecenterRetirementComplete())
                        return false;

                    if (_queued is not { } latest || latest != req)
                        continue;

                    if (!req.CancelAtSessionBoundary)
                        _origin.Recenter(req.DestinationX, req.DestinationY);
                    _originSealed = true;
                }

                bool destSealed = req.CancelAtSessionBoundary
                    ? _paging.TryCancelOriginRecenter()
                    : _paging.TryCommitOriginRecenter(
                        req.DestinationX,
                        req.DestinationY,
                        req.IsSealedDungeon);
                if (!destSealed)
                    return false;

                if (_queued is not { } committed || committed != req)
                {
                    _originSealed = false;
                    _paging.BeginOriginRecenter();
                    continue;
                }

                _queued = null;
                _originSealed = false;
                _admitSubstitute = false;

                if (_substitute is not { } substitute)
                    return true;

                _substitute = null;
                _queued = substitute;
                _srcWasSealedDungeon = _paging.IsCollapsedToDungeon;
                _paging.BeginOriginRecenter();
            }

            return true;
        }
        finally
        {
            _advancing = false;
        }
    }

    public bool Reset(bool sessEnding = false)
    {
        if (_queued is null)
        {
            if (!sessEnding)
                return true;

            _queued = new RequestDef(
                _origin.CenterX,
                _origin.CenterY,
                IsSealedDungeon: false,
                CancelAtSessionBoundary: true);
            _substitute = null;
            _originSealed = false;
            _admitSubstitute = false;
            _srcWasSealedDungeon = _paging.IsCollapsedToDungeon;
            _paging.BeginOriginRecenter();
            return Advance();
        }

        _substitute = null;
        _admitSubstitute = true;
        if (!_originSealed)
        {
            _queued = new RequestDef(
                _origin.CenterX,
                _origin.CenterY,
                IsSealedDungeon: !sessEnding && _srcWasSealedDungeon,
                CancelAtSessionBoundary: sessEnding);
        }
        else if (sessEnding)
        {
            _queued = new RequestDef(
                _origin.CenterX,
                _origin.CenterY,
                IsSealedDungeon: false,
                CancelAtSessionBoundary: true);
        }

        return Advance();
    }
}
