using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace MacAC.Client.Secrets;

// A credential could not be resolved; the launcher reads exit code 2 and the logged reason
internal sealed class AppSecretException : Exception
{
    internal AppSecretException(string msg)
        : base(msg)
    {
    }

    internal AppSecretException(string msg, Exception interiorException)
        : base(msg, interiorException)
    {
    }
}

// A resolved password held in a char buffer that is zeroed on dispose
internal sealed class AppSecretSecret : IDisposable
{
    private char[]? _chars;

    internal AppSecretSecret(string referenceIdent, ReadOnlySpan<char> val)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceIdent);
        if (val.IsEmpty)
            throw new AppSecretException($"Credential '{referenceIdent}' resolved to an empty secret");

        ReferenceIdent = referenceIdent;
        _chars = val.ToArray();
    }

    internal string ReferenceIdent { get; }
    internal bool IsDestroyed => _chars is null;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _chars, null) is { } chars)
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(chars.AsSpan()));
    }

    public override string ToString() => $"[redacted:{ReferenceIdent}]";

    internal string Reveal()
    {
        ObjectDisposedException.ThrowIf(_chars is null, this);
        return new string(_chars);
    }
}
