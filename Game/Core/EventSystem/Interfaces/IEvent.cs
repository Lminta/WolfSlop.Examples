namespace WolfNET.Game.Core.EventSystem.Interfaces
{
    public interface IEvent<TDerived> where TDerived : IEventSystem<TDerived>
    {
    }
}