using WolfNET.Game.Player;

namespace WolfNET.Graphics;

public interface ISceneRenderer
{
    Task PrepareSceneAsync(RenderMap map, CancellationToken cancellationToken = default,
        Func<string, Task>? reportProgress = null, ICharacterAppearance? character = null);
    void Render(RenderFrame frame);
    void ReleaseScene();
}
