namespace WolfNET.Game;

public abstract class ComponentBase
{
    public Guid Id = Guid.NewGuid();
    public ObjectBase Parent { get; }
    
    public ComponentBase(ObjectBase parent)
    {
        Parent = parent;
    }
    
    public virtual void Update(float deltaTime)
    {}
}