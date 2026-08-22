namespace NovaMacro.Core;

public interface IKeyboardOutput
{
    void KeyDown(KeyboardKey key);

    void KeyUp(KeyboardKey key);
}
