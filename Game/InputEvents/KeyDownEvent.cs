using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Game.EventSystem.Interfaces;

namespace WolfNET.Game.InputEvents;

public record KeyDownEvent : IEvent<InputEventSystem>
{
    public required KeyboardEvent Event { get; init; }
}