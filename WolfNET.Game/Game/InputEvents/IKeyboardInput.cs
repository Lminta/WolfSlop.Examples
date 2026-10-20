namespace WolfNET.Game.InputEvents;

public interface IKeyboardInput
{
    KeyState KeyState { get; }
    bool ConsumeJump();
    bool ConsumeViewToggle();
    bool ConsumeAction();
}
