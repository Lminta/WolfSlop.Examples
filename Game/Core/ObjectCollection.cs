namespace WolfNET.Game.Core;

public class ObjectCollection : IDisposable
{
    Dictionary<Guid, ObjectBase> objects = new ();

    public void Update(float deltaTime)
    {
        foreach (var (_, obj) in objects)
        {
            obj.Update(deltaTime);
        }
    }

    public void Dispose()
    {
        foreach (var obj in objects.Values) obj.Dispose();
        objects.Clear();
    }

    public void Add(ObjectBase obj)
    {
        objects.TryAdd(obj.Id, obj);
    }
}