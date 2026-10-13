namespace WolfNET.Game;

public class ObjectCollection
{
    Dictionary<Guid, ObjectBase> objects = new ();

    public void Update(float deltaTime)
    {
        foreach (var (_, obj) in objects)
        {
            obj.Update(deltaTime);
        }
    }

    public void Add(ObjectBase obj)
    {
        objects.TryAdd(obj.Id, obj);
    }
}