using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Game.Core.EventSystem.Interfaces;

namespace WolfNET.Game.InputEvents;

public record KeyUpEvent : IEvent<InputEventSystem>
{
    public required KeyboardEvent Event { get; init; }
}