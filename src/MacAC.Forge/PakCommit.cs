using MacAC.Host;

namespace MacAC.Forge;

public static class PakCommit
{
    internal const string LoadingMarker = ".macac-bake.";

    public static TResult Run<TResult>(
        string destTrail,
        Func<string, TResult> emitLined,
        Action<string, TResult> proveLined,
        CancellationToken abortTicket = default)
        => Run(destTrail, emitLined, proveLined, null, null, abortTicket);

    internal static TResult Run<TResult>(
        string destTrail,
        Func<string, TResult> emitLined,
        Action<string, TResult> proveLined,
        Action? priorTenancy,
        Action? priorSwap,
        CancellationToken abortTicket = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destTrail);
        ArgumentNullException.ThrowIfNull(emitLined);
        ArgumentNullException.ThrowIfNull(proveLined);

        string dest = Path.GetFullPath(destTrail);
        string folder = Path.GetDirectoryName(dest) is { Length: > 0 } ancestor
            ? ancestor
            : throw new InvalidOperationException("destination has no parent directory");
        Directory.CreateDirectory(folder);

        string lined = LoadingTrailFor(dest, Guid.NewGuid());
        try
        {
            abortTicket.ThrowIfCancellationRequested();
            TResult outcome = emitLined(lined);
            abortTicket.ThrowIfCancellationRequested();
            proveLined(lined, outcome);
            abortTicket.ThrowIfCancellationRequested();
            priorTenancy?.Invoke();
            using var tenancy = PublishLease.GrabIfLoaded(dest, abortTicket);
            abortTicket.ThrowIfCancellationRequested();
            priorSwap?.Invoke();
            abortTicket.ThrowIfCancellationRequested();

            File.Move(lined, dest, overwrite: true);
            return outcome;
        }
        finally
        {
            Sweep(lined);
        }
    }

    internal static string LoadingTrailFor(string destTrail, Guid transactionIdent)
    {
        string dest = Path.GetFullPath(destTrail);
        string folder = Path.GetDirectoryName(dest)
            ?? throw new InvalidOperationException("destination has no parent directory");
        return Path.Combine(
            folder,
            $".{Path.GetFileName(dest)}{LoadingMarker}{transactionIdent:N}.tmp");
    }

    private static void Sweep(string lined)
    {
        try
        {
            if (File.Exists(lined))
                File.Delete(lined);
        }
        catch
        {
            // Keep the original exception. A leftover .tmp is recognizable
            // and is never mistaken for a published pak.
        }
    }
}

internal static class PublishLease
{
    private static readonly TimeSpan Backoff = TimeSpan.FromMilliseconds(50);

    internal static IDisposable? GrabIfLoaded(string productTrail, CancellationToken abortTicket)
    {
        string? nonce = Environment.GetEnvironmentVariable(PublishHandshake.NonceVariable);
        if (nonce is null)
            return null;
        if (!PublishHandshake.LooksLikeNonce(nonce))
            throw new InvalidOperationException("The launcher bake publication nonce is not valid");

        string mutexTrail = PublishHandshake.LatchFileFor(productTrail);
        Directory.CreateDirectory(
            Path.GetDirectoryName(mutexTrail)
            ?? throw new InvalidOperationException("The bake publication lock has no parent directory"));

        FileStream tenancy = Grip(mutexTrail, abortTicket);
        try
        {
            string ticketTrail = PublishHandshake.TicketFileFor(productTrail);
            string granted = File.Exists(ticketTrail) ? File.ReadAllText(ticketTrail) : string.Empty;
            if (!string.Equals(granted, nonce, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "This bake process is no longer authorized to publish its output");
            }

            return tenancy;
        }
        catch
        {
            tenancy.Dispose();
            throw;
        }
    }

    // Spins on the exclusive lock file until it opens or the token cancels
    private static FileStream Grip(string mutexTrail, CancellationToken abortTicket)
    {
        while (true)
        {
            abortTicket.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    mutexTrail,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    options: FileOptions.None);
            }
            catch (IOException)
            {
                abortTicket.WaitHandle.WaitOne(Backoff);
            }
        }
    }
}
