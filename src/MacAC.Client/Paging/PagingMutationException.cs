namespace MacAC.Client.Paging;

public sealed class PagingMutationException(
    string msg,
    bool alterationSealed,
    Exception? interiorException = null) : Exception(msg, interiorException)
{
    public bool MutationSealed { get; } = alterationSealed;
}
