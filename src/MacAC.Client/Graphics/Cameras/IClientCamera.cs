using System.Numerics;

namespace MacAC.Client.Graphics;

public interface IClientCamera
{
    Matrix4x4 View { get; }
    Matrix4x4 Projection { get; }
    float Aspect { get; set; }
}
