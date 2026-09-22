using MacAC.Client.Bootstrap;
using MacAC.Client.Machine;

namespace MacAC.Client.Secrets;

internal sealed class AppSecretPicker
{
    // Any group/other bit on a credential file is a refusal on Unix hosts
    private const UnixFileMode SharedBitset =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    private readonly TextReader _stdin;
    private readonly string _fileTrunk;
    private readonly bool _unixHub;

    internal AppSecretPicker(TextReader standardInput, string credentialBaseFolder, bool isUnix)
    {
        _stdin = standardInput ?? throw new ArgumentNullException(nameof(standardInput));
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialBaseFolder);
        _fileTrunk = Path.GetFullPath(credentialBaseFolder);
        _unixHub = isUnix;
    }

    internal AppSecretSecret Resolve(string sessIdent, SessionSecretSpec credential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessIdent);
        ArgumentNullException.ThrowIfNull(credential);

        string val;
        try
        {
            val = credential.Provider switch
            {
                SessionSecretSupplierKind.Environment => FromSurroundings(credential.Reference),
                SessionSecretSupplierKind.StandardInput => FromStandardFeed(credential.Reference),
                SessionSecretSupplierKind.File => FromFile(credential.Reference),
                _ => throw new AppSecretException($"Session '{sessIdent}' uses an not supported credential provider"),
            };
        }
        catch (AppSecretException)
        {
            throw;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new AppSecretException(
                $"Credential '{credential.Reference}' for session '{sessIdent}' could not be resolved",
                problem);
        }

        try
        {
            return new AppSecretSecret(credential.Reference, val.AsSpan());
        }
        finally
        {
            val = string.Empty;
        }
    }

    private static string FromSurroundings(string reference)
    {
        return Environment.GetEnvironmentVariable(reference) is { Length: > 0 } val
            ? val
            : throw new AppSecretException($"Credential environment reference '{reference}' is not available");
    }

    private string FromStandardFeed(string reference)
    {
        return _stdin.ReadLine() is { Length: > 0 } val
            ? val
            : throw new AppSecretException($"Credential standard-input reference '{reference}' is not available");
    }

    private string FromFile(string reference)
    {
        string trail = Path.GetFullPath(reference, _fileTrunk);
        if (new FileInfo(trail).LinkTarget is not null)
            throw new AppSecretException($"Credential file reference '{reference}' can't be a symbolic link");

        if (EnginePlatformWarden.IsUnixCore && _unixHub)
        {
            var manner = File.GetUnixFileMode(trail);
            if ((manner & SharedBitset) != 0 || (manner & UnixFileMode.UserRead) == 0)
                throw new AppSecretException($"Credential file reference '{reference}' has to be readable only by its owner");
        }

        string val = File.ReadAllText(trail).TrimEnd('\r', '\n');
        return val.Length is 0 ? throw new AppSecretException($"Credential file reference '{reference}' is empty") : val;
    }
}
