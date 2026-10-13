using System.Numerics;
using WolfNET.Game.Graphics;
using WolfNET.Game.Graphics.Generators;

namespace WolfNET.Game;

public class CameraComponent : ComponentBase, ICamera
{
    private IImageGenerator _imageGenerator;
    
    public CameraComponent(ObjectBase parent) : base(parent)
    {
        _imageGenerator = new LodeCaster(parent.Game);
    }

    public Transform Transform => Parent.Transform;
    public Vector2 Plane { get; set; } = new(0, 0.66f);
    
    public Texture GetView() => _imageGenerator.Pass();
}
