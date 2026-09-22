using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacAC.Client.Bootstrap;

// A --session-config document is well-formed JSON but not a valid graphical session
internal sealed class SessionSetupException : Exception
{
    internal SessionSetupException(string msg)
        : base(msg)
    {
    }

    internal SessionSetupException(string msg, Exception interiorException)
        : base(msg, interiorException)
    {
    }
}

internal static class SessionSetupFetcher
{
    private const int LatestVer = 1;

    private static readonly JsonSerializerOptions Strict = new()
    {
        AllowTrailingCommas = false,
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    internal static (SessionSetup Configuration, SessionSpec Session) Load(string trail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trail);

        using FileStream flow = File.OpenRead(Path.GetFullPath(trail));
        SessionSetup rig = JsonSerializer.Deserialize<SessionSetup>(flow, Strict)
            ?? throw new SessionSetupException("The configuration document is empty");

        Demand(
            rig.Version == LatestVer,
            $"Unsupported configuration version {rig.Version}; expected {LatestVer}.");
        Demand(
            rig.Sessions is { Count: 1 },
            "The graphical host requires exactly one configured session.");
        SessionSpec sess = rig.Sessions![0]
            ?? throw new SessionSetupException("The configured session can't be null");

        VerifySubstance(rig.Process?.Content);
        VerifySess(sess);
        return (rig, sess);
    }

    private static void Demand(bool holds, string complaint)
    {
        if (!holds)
            throw new SessionSetupException(complaint);
    }

    private static void VerifySubstance(SessionContentSpec? substance)
    {
        if (substance is null)
            return;
        // A prepared package is optional: without one the content is built from the dats.
        Demand(
            !string.IsNullOrWhiteSpace(substance.DatDirectory),
            "process.content requires a non-empty datDirectory.");

        bool topLayer = !string.IsNullOrWhiteSpace(substance.PreparedAssetOverlayPath);
        VerifySubstanceRest(substance, topLayer);
    }

    private static void VerifySubstanceRest(SessionContentSpec substance, bool topLayer)
    {
        bool baseRecipe = substance.PreparedAssetBaseRecipeVersion is > 0;
        bool netRecipe = substance.PreparedAssetEffectiveRecipeVersion is > 0;
        Demand(
                    topLayer == baseRecipe && topLayer == netRecipe,
                    "process.content overlay path, base recipe, and effective recipe must be supplied together.");
    }

    private static void VerifySess(SessionSpec sess)
    {
        Demand(!string.IsNullOrWhiteSpace(sess.Id), "The session requires a non-empty id.");
        string ident = sess.Id;
        Demand(
            sess.Endpoint is not null
            && !string.IsNullOrWhiteSpace(sess.Endpoint.Host)
            && sess.Endpoint.Port is >= 1 and <= 65535,
            $"Session '{ident}' requires a host and a port from 1 through 65535.");
        VerifySessRest(sess, ident);
    }

    private static void VerifySessRest(SessionSpec sess, string ident)
    {
        Demand(!string.IsNullOrWhiteSpace(sess.Account), $"Session '{ident}' requires a non-empty account.");
        if (sess.Character is { } choose)
        {
            int selectors =
                (choose.Index.HasValue ? 1 : 0)
                + (choose.Id.HasValue ? 1 : 0)
                + (!string.IsNullOrWhiteSpace(choose.Name) ? 1 : 0);
            Demand(
                selectors is 1 && choose.Index is not < 0 && choose.Id != 0u,
                $"Session '{ident}' character selector must specify exactly one valid index, id, or name.");
        }
        VerifySessTail(sess, ident);
    }

    private static void VerifySessTail(SessionSpec sess, string ident)
    {
        Demand(
                sess.Credential is not null && !string.IsNullOrWhiteSpace(sess.Credential.Reference),
                $"Session '{ident}' requires a credential reference.");
        if (sess.Plugins is { } extensions)
        {
            foreach (string? plugin in extensions)
                Demand(!string.IsNullOrWhiteSpace(plugin), $"Session '{ident}' plugins entries must be non-empty strings.");
        }
        Demand(sess.LoginCommandDelayMs >= 0, $"Session '{ident}' loginCommandDelayMs must be non-negative.");
        Demand(
                    sess.StatusFile is null || !string.IsNullOrWhiteSpace(sess.StatusFile),
                    $"Session '{ident}' statusFile must be a non-empty path when present.");
        if (sess.Mode is { } manner)
        {
            Demand(
                !string.Equals(manner, "probe", StringComparison.Ordinal),
                $"Session '{ident}' has mode 'probe'; probe sessions are headless-only and cannot run on the graphical host.");
            throw new SessionSetupException($"Session '{ident}' has not supported mode '{manner}'.");
        }
    }
}
