namespace WolfNET.Game.EventSystem.Interfaces
{
    public interface IEvent<TDerived> where TDerived : IEventSystem<TDerived>
    {
    }
}