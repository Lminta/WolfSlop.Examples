using System.Numerics;

namespace WolfNET.Game;

// TextureLayer is zero-based, matching WebGpuRenderer.TexturePaths.
public readonly record struct Sprite(Vector2 Position, uint TextureLayer);
