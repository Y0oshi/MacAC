namespace MacAC.Client.Graphics.Tenancy;

internal sealed class DelegateTenancyDomainSource(
    TenancyDomain domain,
    Func<TenancyDomainCapture> capture) : ITenancyDomainSource
{
    private readonly Func<TenancyDomainCapture> _grab =
        capture ?? throw new ArgumentNullException(nameof(capture));

    public TenancyDomain Domain { get; } = domain;

    public TenancyDomainCapture GrabResidency()
    {
        var capture = _grab();
        if (capture.Domain != Domain)
        {
            throw new InvalidOperationException(
                $"Residency source {Domain} returned {capture.Domain}.");
        }
        capture.Validate();
        return capture;
    }
}
