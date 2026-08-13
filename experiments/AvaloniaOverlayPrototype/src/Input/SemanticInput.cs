namespace GameBarAlternative.AvaloniaPrototype.Input;

public enum SemanticInput
{
    Up,
    Down,
    Left,
    Right,
    Activate,
    Back,
}

public enum SemanticInputSource
{
    Keyboard,
    Controller,
}

public sealed class SemanticInputEventArgs(SemanticInput input, SemanticInputSource source) : EventArgs
{
    public SemanticInput Input { get; } = input;

    public SemanticInputSource Source { get; } = source;
}
