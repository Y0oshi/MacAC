namespace MacAC.Host;

public static class PublishHandshake
{
    public const string NonceVariable = "MACAC_BAKE_PUBLISH_NONCE_V1";
    public const string LockSuffix = ".publish.lock";
    public const string TicketSuffix = ".publish-token";

    public static string MintNonce() => Guid.NewGuid().ToString("N");

    public static bool LooksLikeNonce(string? contender)
    {
        if (contender is not { Length: 32 })
            return false;
        if (!Guid.TryParseExact(contender, "N", out Guid decoded))
            return false;
        return string.Equals(
            decoded.ToString("N"),
            contender,
            StringComparison.Ordinal);
    }

    public static string LatchFileFor(string productTrail) =>
        Beside(productTrail, LockSuffix);

    public static string TicketFileFor(string productTrail) =>
        Beside(productTrail, TicketSuffix);

    private static string Beside(string productTrail, string suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productTrail);
        return Path.GetFullPath(productTrail) + suffix;
    }
}
