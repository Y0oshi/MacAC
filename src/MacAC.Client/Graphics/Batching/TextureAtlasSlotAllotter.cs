namespace MacAC.Client.Graphics.Batching;

internal sealed class TextureAtlasSlotAllotter
{
    private readonly bool[] _rented;
    private readonly Stack<int> _returned = new();
    private int _upcoming;

    public TextureAtlasSlotAllotter(int cap)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cap, 1);
        _rented = new bool[cap];
    }

    public int OnHandTally => _returned.Count + (_rented.Length - _upcoming);
    public int Capacity => _rented.Length;

    public int Rent()
    {
        int socket;
        if (_returned.Count is not 0)
        {
            socket = _returned.Pop();
        }
        else
        {
            if (_upcoming == _rented.Length)
                throw new InvalidOperationException(
                    $"Texture atlas has no GPU-safe layer available ({_rented.Length} layers)"
                );
            socket = _upcoming++;
        }

        if (_rented[socket])
            throw new InvalidOperationException($"Texture atlas layer {socket} was rented twice");
        _rented[socket] = true;
        return socket;
    }

    public void Yield(int socket)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(socket);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(socket, _upcoming);
        if (!_rented[socket])
            throw new InvalidOperationException($"Texture atlas layer {socket} was returned twice");

        _rented[socket] = false;
        _returned.Push(socket);
    }
}
