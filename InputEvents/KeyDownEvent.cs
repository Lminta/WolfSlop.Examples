using EventSystem.Interfaces;
using SpawnDev.BlazorJS.JSObjects;

namespace WolfNET.InputEvents;

public record KeyDownEvent : IEvent<InputEventSystem>
{
    public required KeyboardEvent Event { get; init; }
}