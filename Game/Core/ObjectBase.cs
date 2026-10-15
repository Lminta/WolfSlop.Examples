using System.Numerics;

namespace WolfNET.Game.Core;

public abstract class ObjectBase
{
    public Guid Id = Guid.NewGuid();
    public Wolf Game { get; }
    
    public ObjectBase(Wolf game)
    {
        Game = game;
    }
    
    public Dictionary<Guid, ComponentBase> Components { get; } = new();
    
    public Transform Transform { get; set; } = new()
    {
        Position = new Vector2(0, 0),
        Rotation = new Vector2(0, 0),
    };

    public virtual void Update(float deltaTime)
    {
        foreach (var component in Components.Values)
        {
            component.Update(deltaTime);
        }
    }
    
    public void AddComponent(ComponentBase component)
    {
        Components.TryAdd(component.Id, component);
    }
}