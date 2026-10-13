namespace WolfNET.Game.EventSystem.Interfaces
{
    public interface IEventListener<TDerived, TEvent> where TDerived : IEventSystem<TDerived>
        where TEvent : IEvent<TDerived>
    {
        Task OnEvent(TEvent gameEvent);
    }
}