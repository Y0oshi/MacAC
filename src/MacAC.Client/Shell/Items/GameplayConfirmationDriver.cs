using MacAC.Client.Shell.Panels;
using MacAC.Wire.Messages;

namespace MacAC.Client.Shell;

public sealed class GameplayConfirmationDriver : IDisposable
{
    private readonly CanonPromptMint _popups;
    private readonly Action<uint, uint, bool> _transmitResponse;
    private readonly Func<uint, string, string?>? _constructMsg;
    private uint _srvKind;
    private uint _srvCtx;
    private bool _destroyed;

    public GameplayConfirmationDriver(
        CanonPromptMint dialogs,
        Action<uint, uint, bool> sendResponse,
        Func<uint, string, string?>? constructMsg = null)
    {
        _popups = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _transmitResponse = sendResponse ?? throw new ArgumentNullException(nameof(sendResponse));
        _constructMsg = constructMsg;
        _popups.DialogClosed += OnPopupClosed;
    }

    public uint EngagedPopupCtx { get; private set; }

    public bool ProcessReq(PlaySignals.ToonAckRequest req)
    {
        _srvKind = req.Type;
        _srvCtx = req.ContextId;

        if (EngagedPopupCtx is not 0u)
            return false;

        string msg = req.Type is 2u or 3u or 5u or 6u
            ? req.Message + " Continue?"
            : _constructMsg?.Invoke(req.Type, req.Message)
                ?? req.Message;
        CanonPromptData blob = CanonPromptData.Confirmation(msg);
        EngagedPopupCtx = _popups.MakeDialog(blob);
        return EngagedPopupCtx is not 0u;
    }

    public bool ProcessDone(PlaySignals.ToonAckFinished done)
    {
        return EngagedPopupCtx is 0u
            || done.Type != _srvKind
            || done.ContextId != _srvCtx
            ? false
            : _popups.ShutPopup(EngagedPopupCtx);
    }

    public void ResetSession()
    {
        EngagedPopupCtx = 0u;
        _srvKind = 0u;
        _srvCtx = 0u;
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _popups.DialogClosed -= OnPopupClosed;
    }

    private void OnPopupClosed(uint ctx, CanonPromptData blob)
    {
        if (ctx != EngagedPopupCtx
            || blob.FetchUInt32(CanonPromptProperty.Type) !=
                (uint)CanonPromptType.Confirmation)
            return;

        bool approved = blob.FetchBoolean(CanonPromptProperty.AckOutcome);
        _transmitResponse(_srvKind, _srvCtx, approved);
        EngagedPopupCtx = 0u;
        _srvKind = 0u;
        _srvCtx = 0u;
    }
}
