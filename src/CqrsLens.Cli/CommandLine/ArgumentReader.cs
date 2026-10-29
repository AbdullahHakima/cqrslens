namespace CqrsLens;

/// <summary>
/// Walks the raw argument array and owns the "option requires a value" rule so
/// <see cref="CliParser"/> stays focused on mapping flags to <see cref="CliOptions"/>.
/// </summary>
internal sealed class ArgumentReader
{
    private readonly string[] _args;
    private int _index = -1;

    public ArgumentReader(string[] args) => _args = args;

    public bool MoveNext() => ++_index < _args.Length;

    public string Current => _args[_index];

    public string ReadValue(string option, string expected)
    {
        if (_index + 1 >= _args.Length)
            throw new CliParseException($"error: {option} needs {expected}");

        return _args[++_index];
    }
}
