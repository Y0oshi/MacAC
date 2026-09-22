using System.Numerics;

#nullable disable

namespace MacAC.Dat;

// A cue fires at a frame of an animation (or a step of an effect script): play a sound, swap a part, spawn particles...
// On disk: kind (u32), direction (u32), then the kind's own payload.
public abstract class Cue
{
    public CueDirection Direction { get; set; }
    public abstract CueKind Kind { get; }

    // Throws on a kind the game never wrote, so a misaligned read fails instead of yielding garbage.
    public static Cue Read(ref DatCursor c)
    {
        var kind = (CueKind)c.U32();
        var dir = (CueDirection)c.U32();
        Cue cue = kind switch
        {
            CueKind.Sound => new SoundCue { Direction = dir, WaveId = c.U32() },
            CueKind.SoundTable => new SoundTableCue { Direction = dir, Sound = (SoundTag)c.U32() },
            CueKind.Attack => new AttackCue { Direction = dir, Cone = AttackCone.Read(ref c) },
            CueKind.AnimationDone => new AnimationDoneCue { Direction = dir },
            CueKind.ReplaceObject => new ReplaceObjectCue { Direction = dir, PartIndex = c.U16(), PartId = c.PackedId(DatIds.PartMesh) },
            CueKind.Ethereal => new EtherealCue { Direction = dir, Ethereal = c.Flag32() },
            CueKind.TransparentPart => new TransparentPartCue { Direction = dir, PartIndex = c.U32(), Start = c.F32(), End = c.F32(), Time = c.F32() },
            CueKind.Luminous => new LuminousCue { Direction = dir, Start = c.F32(), End = c.F32(), Time = c.F32() },
            CueKind.LuminousPart => new LuminousPartCue { Direction = dir, PartIndex = c.U32(), Start = c.F32(), End = c.F32(), Time = c.F32() },
            CueKind.Diffuse => new DiffuseCue { Direction = dir, Start = c.F32(), End = c.F32(), Time = c.F32() },
            CueKind.DiffusePart => new DiffusePartCue { Direction = dir, PartIndex = c.U32(), Start = c.F32(), End = c.F32(), Time = c.F32() },
            CueKind.Scale => new ScaleCue { Direction = dir, End = c.F32(), Time = c.F32() },
            CueKind.CreateParticle => new CreateParticleCue { Direction = dir, EmitterSpecId = c.U32(), PartIndex = c.U32(), Offset = Pose.Read(ref c), EmitterId = c.U32() },
            CueKind.DestroyParticle => new DestroyParticleCue { Direction = dir, EmitterId = c.U32() },
            CueKind.StopParticle => new StopParticleCue { Direction = dir, EmitterId = c.U32() },
            CueKind.NoDraw => new NoDrawCue { Direction = dir, NoDraw = c.Flag32() },
            CueKind.DefaultScript => new DefaultScriptCue { Direction = dir },
            CueKind.DefaultScriptPart => new DefaultScriptPartCue { Direction = dir, PartIndex = c.U32() },
            CueKind.CallPES => new CallEffectCue { Direction = dir, EffectId = c.U32(), Pause = c.F32() },
            CueKind.Transparent => new TransparentCue { Direction = dir, Start = c.F32(), End = c.F32(), Time = c.F32() },
            CueKind.SoundTweaked => new SoundTweakedCue { Direction = dir, WaveId = c.U32(), Priority = c.F32(), Probability = c.F32(), Volume = c.F32() },
            CueKind.SetOmega => new SetOmegaCue { Direction = dir, Axis = c.Vec3() },
            CueKind.TextureVelocity => new TextureVelocityCue { Direction = dir, USpeed = c.F32(), VSpeed = c.F32() },
            CueKind.TextureVelocityPart => new TextureVelocityPartCue { Direction = dir, PartIndex = c.U32(), USpeed = c.F32(), VSpeed = c.F32() },
            CueKind.SetLight => new SetLightCue { Direction = dir, LightsOn = c.Flag32() },
            CueKind.CreateBlockingParticle => new CreateBlockingParticleCue { Direction = dir, EmitterSpecId = c.U32(), PartIndex = c.U32(), Offset = Pose.Read(ref c), EmitterId = c.U32() },
            _ => throw new DatFormatException($"unknown animation cue kind 0x{(uint)kind:X8} in 0x{c.FileId:X8} at {c.Offset - 8}"),
        };
        return cue;
    }
}

public class AttackCone
{
    public uint PartIndex { get; set; }
    public float LeftX { get; set; }
    public float LeftY { get; set; }
    public float RightX { get; set; }
    public float RightY { get; set; }
    public float Radius { get; set; }
    public float Height { get; set; }
    public static AttackCone Read(ref DatCursor c) => new()
    {
        PartIndex = c.U32(), LeftX = c.F32(), LeftY = c.F32(), RightX = c.F32(), RightY = c.F32(), Radius = c.F32(), Height = c.F32(),
    };
}

public class SoundCue : Cue { public uint WaveId { get; set; } public override CueKind Kind => CueKind.Sound; }
public class SoundTableCue : Cue { public SoundTag Sound { get; set; } public override CueKind Kind => CueKind.SoundTable; }
public class AttackCue : Cue { public AttackCone Cone { get; set; } = null!; public override CueKind Kind => CueKind.Attack; }
public class AnimationDoneCue : Cue { public override CueKind Kind => CueKind.AnimationDone; }
public class ReplaceObjectCue : Cue { public ushort PartIndex { get; set; } public uint PartId { get; set; } public override CueKind Kind => CueKind.ReplaceObject; }
public class EtherealCue : Cue { public bool Ethereal { get; set; } public override CueKind Kind => CueKind.Ethereal; }
public class TransparentPartCue : Cue { public uint PartIndex { get; set; } public float Start { get; set; } public float End { get; set; } public float Time { get; set; } public override CueKind Kind => CueKind.TransparentPart; }
public class LuminousCue : Cue { public float Start { get; set; } public float End { get; set; } public float Time { get; set; } public override CueKind Kind => CueKind.Luminous; }
public class LuminousPartCue : Cue { public uint PartIndex { get; set; } public float Start { get; set; } public float End { get; set; } public float Time { get; set; } public override CueKind Kind => CueKind.LuminousPart; }
public class DiffuseCue : Cue { public float Start { get; set; } public float End { get; set; } public float Time { get; set; } public override CueKind Kind => CueKind.Diffuse; }
public class DiffusePartCue : Cue { public uint PartIndex { get; set; } public float Start { get; set; } public float End { get; set; } public float Time { get; set; } public override CueKind Kind => CueKind.DiffusePart; }
public class ScaleCue : Cue { public float End { get; set; } public float Time { get; set; } public override CueKind Kind => CueKind.Scale; }
public class CreateParticleCue : Cue { public uint EmitterSpecId { get; set; } public uint PartIndex { get; set; } public Pose Offset { get; set; } = null!; public uint EmitterId { get; set; } public override CueKind Kind => CueKind.CreateParticle; }
public class DestroyParticleCue : Cue { public uint EmitterId { get; set; } public override CueKind Kind => CueKind.DestroyParticle; }
public class StopParticleCue : Cue { public uint EmitterId { get; set; } public override CueKind Kind => CueKind.StopParticle; }
public class NoDrawCue : Cue { public bool NoDraw { get; set; } public override CueKind Kind => CueKind.NoDraw; }
public class DefaultScriptCue : Cue { public override CueKind Kind => CueKind.DefaultScript; }
public class DefaultScriptPartCue : Cue { public uint PartIndex { get; set; } public override CueKind Kind => CueKind.DefaultScriptPart; }
public class CallEffectCue : Cue { public uint EffectId { get; set; } public float Pause { get; set; } public override CueKind Kind => CueKind.CallPES; }
public class TransparentCue : Cue { public float Start { get; set; } public float End { get; set; } public float Time { get; set; } public override CueKind Kind => CueKind.Transparent; }
public class SoundTweakedCue : Cue { public uint WaveId { get; set; } public float Priority { get; set; } public float Probability { get; set; } public float Volume { get; set; } public override CueKind Kind => CueKind.SoundTweaked; }
public class SetOmegaCue : Cue { public Vector3 Axis { get; set; } public override CueKind Kind => CueKind.SetOmega; }
public class TextureVelocityCue : Cue { public float USpeed { get; set; } public float VSpeed { get; set; } public override CueKind Kind => CueKind.TextureVelocity; }
public class TextureVelocityPartCue : Cue { public uint PartIndex { get; set; } public float USpeed { get; set; } public float VSpeed { get; set; } public override CueKind Kind => CueKind.TextureVelocityPart; }
public class SetLightCue : Cue { public bool LightsOn { get; set; } public override CueKind Kind => CueKind.SetLight; }
// Like CreateParticle, but the animation waits for the emitter to finish. The origin library reads it without a payload; retail data carries one.
public class CreateBlockingParticleCue : CreateParticleCue { public override CueKind Kind => CueKind.CreateBlockingParticle; }

// Id prefixes by record family; packed ids are stored relative to these.
public static class DatIds
{
    public const uint PartMesh = 0x01000000;
    public const uint Rig = 0x02000000;
    public const uint MotionClip = 0x03000000;
    public const uint ColorTable = 0x04000000;
    public const uint SkinTexture = 0x05000000;
    public const uint Bitmap = 0x06000000;
    public const uint Skin = 0x08000000;
    public const uint MotionBook = 0x09000000;
    public const uint SoundClip = 0x0A000000;
    public const uint ColorTableSet = 0x0F000000;
    public const uint Wardrobe = 0x10000000;
    public const uint LodTable = 0x11000000;
    public const uint SceneryList = 0x12000000;
    public const uint InteriorShell = 0x0D000000;
    public const uint WorldRegion = 0x13000000;
    public const uint SoundBook = 0x20000000;
    public const uint EffectScript = 0x33000000;
    public const uint EffectBook = 0x34000000;
    public const uint EmitterDesc = 0x32000000;

    public static uint PrefixOf(uint id) => id & 0xFF000000;
}
