using System.Diagnostics.CodeAnalysis;
using MacAC.Dat;

namespace MacAC.Mechanics.Data;

public interface IDatRecordSource
{
    [return: MaybeNull]
    T Get<T>(uint fileIdent) where T : IDatRecord;

    bool TryGet<T>(uint fileIdent, [MaybeNullWhen(false)] out T val) where T : IDatRecord;
}
