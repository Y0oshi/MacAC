using System.Numerics;

namespace MacAC.Client.Graphics.Batching
{
    public class DebugRenderPreferences
    {
        public bool UnhideBoundingBboxes { get; set; } = false;
        public bool PickVerts { get; set; } = true;
        public bool PickStructures { get; set; } = true;
        public bool PickStaticObjects { get; set; } = true;
        public bool PickScenery { get; set; } = false;
        public bool PickEnvironChambers { get; set; } = true;
        public bool PickEnvironChamberStaticObjects { get; set; } = true;
        public bool PickGateways { get; set; } = true;
        public bool UnhideDisqualifiedScenery { get; set; } = true;
        public bool ActivateAnisotropicFiltering { get; set; } = true;

        public Vector4 VertTint { get; set; } = new Vector4(0.7882353f, 0.34901962f, 0.2901961f, 1.0f);
        public Vector4 StructureTint { get; set; } = new Vector4(0.76862746f, 0.5803922f, 0.25882354f, 1.0f);
        public Vector4 StaticObjectTint { get; set; } = new Vector4(0.37254903f, 0.88235295f, 0.9019608f, 1.0f);
        public Vector4 SceneryTint { get; set; } = new Vector4(0.45490196f, 0.72156864f, 0.32156864f, 1.0f);
        public Vector4 EnvironChamberTint { get; set; } = new Vector4(0.5294118f, 0.44705883f, 0.7882353f, 1.0f);
        public Vector4 EnvironChamberStaticObjectTint { get; set; } = new Vector4(0f, 0.49803922f, 1f, 1.0f);
        public Vector4 GatewayTint { get; set; } = new Vector4(1f, 0f, 1f, 1.0f);
    }
}
