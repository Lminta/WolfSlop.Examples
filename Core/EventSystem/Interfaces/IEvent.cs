namespace EventSystem.Interfaces
{
    public interface IEvent<TDerived> where TDerived : IEventSystem<TDerived>
    {
    }
}