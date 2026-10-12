using EventSystem.Interfaces;
using SpawnDev.BlazorJS.JSObjects;

namespace WolfNET.InputEvents;

public record KeyUpEvent : IEvent<InputEventSystem>
{
    public required KeyboardEvent Event { get; init; }
}