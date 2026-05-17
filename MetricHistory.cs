namespace LinuxMintSystemMonitor;

internal sealed class MetricHistory : IReadOnlyList<double>
{
    public const int Capacity = 300;
    private readonly double[] _values = new double[Capacity];
    private int _next;

    public int Count { get; private set; }

    public double this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var start = Count == Capacity ? _next : 0;
            return _values[(start + index) % Capacity];
        }
    }

    public double Last => Count == 0 ? 0d : this[Count - 1];

    public void Add(double value)
    {
        _values[_next] = Math.Max(0, value);
        _next = (_next + 1) % Capacity;
        if (Count < Capacity)
        {
            Count++;
        }
    }

    public double MaxValue()
    {
        var max = 0d;
        for (var i = 0; i < Count; i++)
        {
            max = Math.Max(max, this[i]);
        }

        return max;
    }

    public IEnumerator<double> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
        {
            yield return this[i];
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
