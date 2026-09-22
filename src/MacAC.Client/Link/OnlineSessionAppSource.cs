using MacAC.Sim.Presence;
using MacAC.Wire;

namespace MacAC.Client.Link;

internal sealed class OnlineSessionAppSource(
    OnlineSessionDriver session,
    OnlineSessionDirectiveSurface commands)
        : IOnlineInRealmSource,
      IOnlineRealmSessionSource,
      IOnlineWidgetSessionTarget
{
    private readonly OnlineSessionDriver _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly OnlineSessionDirectiveSurface _commands = commands ?? throw new ArgumentNullException(nameof(commands));

    public bool IsInWorld => _session.IsInWorld;

    public RealmSession? LatestSess => _session.LatestSession;

    public IDirectiveBus Commands => _commands;
}

internal sealed class OnlineSessionDirectiveSurface(Func<string, bool>? tryHndExtensionDirective = null) : IPluginDirectiveBus
{
    private readonly object _latch = new();
    private readonly Func<string, bool>? _tryHndExtensionDirective = tryHndExtensionDirective;
    private OnlineSessionDirectiveRouter? _engaged;

    public IOnlineSessionDirectiveRouting Attach(OnlineSessionDirectiveRouter course)
    {
        ArgumentNullException.ThrowIfNull(course);
        lock (_latch)
        {
            if (_engaged is not null)
            {
                throw new InvalidOperationException(
                    "A graphical live-session command route is by now attached");
            }

            _engaged = course;
            return new CourseTenancy(this, course);
        }
    }

    public void Publish<T>(T directive) where T : notnull
    {
        OnlineSessionDirectiveRouter? course;
        lock (_latch)
            course = _engaged;
        course?.Publish(directive);
    }

    public bool TryHndExtensionDirective(string directiveStroke) =>
        _tryHndExtensionDirective?.Invoke(directiveStroke) == true;

    private void Release(OnlineSessionDirectiveRouter anticipated)
    {
        anticipated.Dispose();
        lock (_latch)
        {
            if (ReferenceEquals(_engaged, anticipated))
                _engaged = null;
        }
    }

    private sealed class CourseTenancy(
        OnlineSessionDirectiveSurface holder,
        OnlineSessionDirectiveRouter course)
        : IOnlineSessionDirectiveRouting
    {
        private readonly object _latch = new();
        private OnlineSessionDirectiveSurface? _holder = holder;

        public void Arm() => course.Arm();

        public void Dispose()
        {
            lock (_latch)
            {
                if (_holder is null)
                    return;

                _holder.Release(course);
                _holder = null;
            }
        }
    }
}
