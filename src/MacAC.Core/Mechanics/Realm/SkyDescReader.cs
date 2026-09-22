using System.Globalization;
using System.Numerics;
using MacAC.Dat;
using MacAC.Mechanics.Data;

namespace MacAC.Mechanics.Realm;

/// <summary>One celestial object (sun, moon, cloud layer) as the region's SkyDesc authored it.</summary>
public sealed class SkyObjectRow
{
    private const uint PostTableauBit = 0x01u;
    private const uint WeatherBit = 0x04u;

    public float CommenceMoment;
    public float FinishMoment;
    public float CommenceAngle;
    public float FinishAngle;
    public float BmpVelX;
    public float BmpVelY;
    public uint GfxObjId;
    public uint PesObjectId;
    public uint Properties;
    public uint DefaultProgramIdent;
    public Vector3 AuthoredOrderMiddle;

    public bool IsWeather => (Properties & WeatherBit) is not 0u;

    public bool IsPostTableau => (Properties & PostTableauBit) is not 0u;

    public bool IsVisible(float t)
    {
        if (CommenceMoment == FinishMoment)
            return true;
        return CommenceMoment < FinishMoment
            ? t >= CommenceMoment && t <= FinishMoment
            : t >= CommenceMoment || t <= FinishMoment;
    }

    /// <summary>0..1 through the visibility window; drives the BeginAngle→EndAngle sweep.</summary>
    public float AngleHeadway(float t)
    {
        if (CommenceMoment == FinishMoment)
            return 0f;

        float headway;
        if (CommenceMoment < FinishMoment)
        {
            headway = (t - CommenceMoment) / (FinishMoment - CommenceMoment);
        }
        else
        {
            float span = 1f - CommenceMoment + FinishMoment;
            headway = t >= CommenceMoment ? (t - CommenceMoment) / span : (t + (1f - CommenceMoment)) / span;
        }
        return Math.Clamp(headway, 0f, 1f);
    }

    public float LatestAngle(float t)
    {
        return CommenceMoment == FinishMoment ? CommenceAngle : CommenceAngle + (FinishAngle - CommenceAngle) * AngleHeadway(t);
    }
}

public sealed class SkyObjectSwapRow
{
    public uint ObjectIndex;
    public uint GfxObjId;
    public float Rotate;
    public float Transparent;
    public float Luminosity;
    public float UpperBright;
    public Vector3 AuthoredOrderCenter;
}

public sealed class DatSkyKeyframeRow
{
    public SkyKeyframe Keyframe;
    public IReadOnlyList<SkyObjectSwapRow> Replaces = [];
}

public sealed class DayGroupRow
{
    public float ChanceOfOccur;
    public string Name = "";
    public IReadOnlyList<SkyObjectRow> SkyObjects = [];
    public IReadOnlyList<DatSkyKeyframeRow> HeavensTimes = [];
}

/// <summary>The region's SkyDesc, with the day-group choice for any date.</summary>
public sealed class MountedSkyDesc
{
    private const string ForcedDayClusterVariable = "MACAC_DAY_GROUP";
    private const int SecsPerDay = DerethDateMoment.DaysInAMonth * DerethDateMoment.MonthsInAYear;   // 360

    public double TickSize;
    public double LightTickSize;
    public IReadOnlyList<DayGroupRow> DayGroups = [];

    public DayGroupRow? DefaultDayCluster
    {
        get
        {
            int choose = PickDayClusterOrdinal(year: 0, SecsPerDay, dayOfYear: 0);
            return DayGroups.Count > 0 ? DayGroups[choose] : null;
        }
    }

    public int PickDayClusterOrdinal(int year, int secsPerDay, int dayOfYear)
    {
        // An environment override wins over the calendar.
        int? forced = null;
        if (int.TryParse(System.Environment.GetEnvironmentVariable(ForcedDayClusterVariable), NumberStyles.Integer, CultureInfo.InvariantCulture, out int num)
            && num >= 0 && num < DayGroups.Count)
            forced = num;
        return SkyDayGroupPicker.SelectIndex(DayGroups.Count, year, secsPerDay, dayOfYear, forced);
    }

    public DayGroupRow? ActiveDayCluster(double srvBeats)
    {
        int choose = PickDayClusterOrdinal(
            DerethDateMoment.AbsoluteYear(srvBeats),
            SecsPerDay,
            DerethDateMoment.DayOfYear(srvBeats));
        return choose < DayGroups.Count ? DayGroups[choose] : null;
    }

    public SkyStateSource AssembleDefaultSupplier() => SupplierFor(DefaultDayCluster);

    public SkyStateSource AssembleSupplierForDay(double srvBeats) => SupplierFor(ActiveDayCluster(srvBeats));

    private static SkyStateSource SupplierFor(DayGroupRow? cluster)
    {
        return cluster is null || cluster.HeavensTimes.Count is 0
            ? SkyStateSource.Default()
            : new SkyStateSource(cluster.HeavensTimes.Select(static s => s.Keyframe).ToList());
    }
}

/// <summary>Projects the DAT region's SkyInfo into <see cref="MountedSkyDesc"/>.</summary>
public static class SkyDescReader
{
    public const uint ZoneDatIdent = 0x13000000u;

    private const uint RigIdentChunk = 0x02000000u;
    private const uint GfxObjRefIdentChunk = 0x01000000u;
    private const uint IdentChunkBitmask = 0xFF000000u;
    private const float Pct = 100f;

    public static MountedSkyDesc? PullFromDat(IDatRecordSource datFiles)
    {
        ArgumentNullException.ThrowIfNull(datFiles);
        return datFiles.Get<WorldRegion>(ZoneDatIdent) is { } zone ? PullFromZone(zone, datFiles) : null;
    }

    public static MountedSkyDesc? PullFromZone(WorldRegion zone, IDatRecordSource? datFiles = null)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (!zone.Parts.HasFlag(RegionParts.HasSkyInfo) || zone.Sky is not { } heavens)
            return null;

        if (System.Environment.GetEnvironmentVariable("MACAC_DUMP_SKY") == "1")
            SkyDescDump.Output(zone);

        List<DayGroupRow> clusters = new List<DayGroupRow>(heavens.DayGroups.Count);
        foreach (DayGroup cluster in heavens.DayGroups)
        {
            clusters.Add(new DayGroupRow
            {
                ChanceOfOccur = cluster.ChanceOfOccur,
                Name = cluster.DayName?.ToString() ?? "",
                SkyObjects = cluster.SkyObjects.Select(o => Object(o, datFiles)).ToList(),
                HeavensTimes = cluster.SkyTimes.Select(t => TimeOfDay(t, datFiles)).ToList(),
            });
        }

        return new MountedSkyDesc
        {
            TickSize = heavens.TickSize,
            LightTickSize = heavens.LightTickSize,
            DayGroups = clusters,
        };
    }

    public static Vector3 TintToVec3(Argb? aRGB)
    {
        return aRGB is null ? Vector3.One : new Vector3(aRGB.Red / 255f, aRGB.Green / 255f, aRGB.Blue / 255f);
    }

    private static SkyObjectRow Object(SkyObject s, IDatRecordSource? datFiles)
    {
        uint gfxObjRefIdent = s.PartMeshId;
        return new SkyObjectRow
        {
            CommenceMoment = s.BeginTime,
            FinishMoment = s.EndTime,
            CommenceAngle = s.BeginAngle,
            FinishAngle = s.EndAngle,
            BmpVelX = s.TexVelocityX,
            BmpVelY = s.TexVelocityY,
            GfxObjId = gfxObjRefIdent,
            PesObjectId = s.EffectScriptId,
            Properties = s.Properties,
            DefaultProgramIdent = PrepareProgram(gfxObjRefIdent, datFiles),
            AuthoredOrderMiddle = SortCenter(gfxObjRefIdent, datFiles),
        };
    }

    private static DatSkyKeyframeRow TimeOfDay(SkyTimeOfDay s, IDatRecordSource? datFiles)
    {
        SkyKeyframe keyframe = new SkyKeyframe(
            Begin: s.Begin,
            SunHeadingDeg: s.DirHeading,
            SunPitchDeg: s.DirPitch,
            DirColor: TintToVec3(s.DirColor),
            DirBright: s.DirBright,
            AmbColor: TintToVec3(s.AmbColor),
            AmbBright: s.AmbBright,
            FogColor: TintToVec3(s.WorldFogColor),
            FogDensity: 0f,
            FogStart: s.MinWorldFog,
            FogEnd: s.MaxWorldFog,
            FogMode: s.WorldFog switch
            {
                1u => FogManner.Linear,
                2u => FogManner.Exp,
                3u => FogManner.Exp2,
                _ => FogManner.Off,
            });

        return new DatSkyKeyframeRow
        {
            Keyframe = keyframe,
            Replaces = s.Replacements.Select(replace => new SkyObjectSwapRow
            {
                ObjectIndex = replace.ObjectIndex,
                GfxObjId = replace.PartMeshId,
                Rotate = replace.Rotate,
                Transparent = replace.Transparent / Pct,
                Luminosity = replace.Luminosity / Pct,
                UpperBright = replace.MaxBright / Pct,
                AuthoredOrderCenter = SortCenter(replace.PartMeshId, datFiles),
            }).ToList(),
        };
    }

    private static uint PrepareProgram(uint ident, IDatRecordSource? datFiles)
    {
        if (datFiles is null || (ident & IdentChunkBitmask) != RigIdentChunk)
            return 0u;
        try
        {
            return datFiles.TryGet<RigSpec>(ident, out var rig) && rig is not null ? rig.DefaultEffectId : 0u;
        }
        catch
        {
            return 0u;
        }
    }

    private static Vector3 SortCenter(uint ident, IDatRecordSource? datFiles)
    {
        if (datFiles is null || (ident & IdentChunkBitmask) != GfxObjRefIdentChunk)
            return Vector3.Zero;
        try
        {
            return datFiles.TryGet<PartMesh>(ident, out var gfx) && gfx is not null ? gfx.SortCenter : Vector3.Zero;
        }
        catch
        {
            return Vector3.Zero;
        }
    }
}

// The MACAC_DUMP_SKY=1 console dump of a region's calendar and sky tables
internal static class SkyDescDump
{
    private const string Tag = "[sky-dump]";

    public static void Output(WorldRegion zone)
    {
        if (zone.Sky is not { } heavens)
            return;

        Console.WriteLine($"{Tag} ======== BEGIN SkyDesc dump ========");
        Console.WriteLine($"{Tag} Region Id={zone.Id:X8} Number={zone.RegionNumber} Name=\"{zone.Name}\"");
        DumpCalendar(zone);
        OutputRest(heavens);
    }

    private static void OutputRest(SkyDesc heavens)
    {
        Console.WriteLine($"{Tag} SkyDesc TickSize={heavens.TickSize} LightTickSize={heavens.LightTickSize} DayGroups.Count={heavens.DayGroups.Count}");
        for (int g = 0; g < heavens.DayGroups.Count; ++g)
        {
            DayGroup cluster = heavens.DayGroups[g];
            Console.WriteLine($"{Tag} DayGroup[{g}] Name=\"{cluster.DayName}\" Chance={cluster.ChanceOfOccur:F3} SkyObjects.Count={cluster.SkyObjects.Count} SkyTime.Count={cluster.SkyTimes.Count}");

            for (int idx = 0; idx < cluster.SkyObjects.Count; ++idx)
            {
                SkyObject o = cluster.SkyObjects[idx];
                Console.WriteLine(
                    $"{Tag}   SkyObject[{idx}] GfxObjId=0x{o.PartMeshId:X8} PesObjectId=0x{o.EffectScriptId:X8} " +
                    $"Time=[{o.BeginTime:F4}..{o.EndTime:F4}] Angle=[{o.BeginAngle:F1}°..{o.EndAngle:F1}°] " +
                    $"TexVel=({o.TexVelocityX:F5},{o.TexVelocityY:F5}) Properties=0x{o.Properties:X8}");
            }

            for (int kdx = 0; kdx < cluster.SkyTimes.Count; ++kdx)
            {
                SkyTimeOfDay t = cluster.SkyTimes[kdx];
                Console.WriteLine(
                    $"{Tag}   SkyTime[{kdx}] Begin={t.Begin:F4} " +
                    $"DirBright={t.DirBright:F4} DirHeading={t.DirHeading:F1}° DirPitch={t.DirPitch:F1}° " +
                    $"DirColor={Rgba(t.DirColor)} AmbBright={t.AmbBright:F4} AmbColor={Rgba(t.AmbColor)} " +
                    $"Fog=[{t.MinWorldFog:F1}m..{t.MaxWorldFog:F1}m] FogColor={Rgba(t.WorldFogColor)} FogManner={t.WorldFog}");

                for (int r = 0; r < t.Replacements.Count; ++r)
                {
                    SkyObjectReplace rep = t.Replacements[r];
                    Console.WriteLine(
                        $"{Tag}     Replace[{r}] ObjectIndex={rep.ObjectIndex} GfxObjId=0x{rep.PartMeshId:X8} " +
                        $"Rotate={rep.Rotate:F3}° Transparent_raw={rep.Transparent:F6} " +
                        $"Luminosity_raw={rep.Luminosity:F6} MaxBright_raw={rep.MaxBright:F6}");
                }
            }
        }
        Console.WriteLine($"{Tag} ======== END SkyDesc dump ========");
    }

    private static void DumpCalendar(WorldRegion zone)
    {
        if (zone.Time is not { } time)
        {
            Console.WriteLine($"{Tag} GameTime: null (no calendar info in this region)");
            return;
        }

        Console.WriteLine(
            $"{Tag} GameTime ZeroTimeOfYear={time.ZeroTimeOfYear} ZeroYear={time.ZeroYear} " +
            $"DayLength={time.DayLength} DaysPerYear={time.DaysPerYear} " +
            $"YearSpec=\"{time.YearSpec}\" TimesOfDay.Count={time.TimesOfDay?.Count ?? 0} " +
            $"DaysOfWeek.Count={time.DaysOfWeek?.Count ?? 0} Seasons.Count={time.Seasons?.Count ?? 0}");

        if (time.TimesOfDay is null)
            return;
        for (int idx = 0; idx < time.TimesOfDay.Count; ++idx)
        {
            TimeOfDay t = time.TimesOfDay[idx];
            Console.WriteLine($"{Tag}   TimeOfDay[{idx}] Start={t.Start} IsNight={t.IsNight} Name=\"{t.Name}\"");
        }
    }

    private static string Rgba(Argb? aRGB)
    {
        return aRGB is null ? "null" : $"({aRGB.Red},{aRGB.Green},{aRGB.Blue},{aRGB.Alpha})";
    }
}
