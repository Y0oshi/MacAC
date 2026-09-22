using System.Numerics;
using System.Runtime.InteropServices;

namespace MacAC.Client.Graphics.Batching
{
    /// <summary>Global scene data for Uniform Buffer Object (UBO)</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 16)]
    public struct StageData
    {
        public Matrix4x4 View;          // 64 bytes
        public Matrix4x4 Projection;    // 64 bytes
        public Matrix4x4 LensMirror; // 64 bytes
        public Vector3 CamLocus;   // 12 bytes
        private float _padding1;         // 4 bytes
        public Vector3 LampDir;   // 12 bytes
        private float _padding2;         // 4 bytes
        public Vector3 SunlightTint;    // 12 bytes
        private float _padding3;         // 4 bytes
        public Vector3 AmbientColor;     // 12 bytes
        public float SpecularStrength;      // 4 bytes
        public Vector2 ViewRectDims;     // 8 bytes
        private Vector2 _padding4;       // 8 bytes
    }
}
