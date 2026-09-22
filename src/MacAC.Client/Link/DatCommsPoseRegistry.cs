using MacAC.Dat;
using MacAC.Assets;
using DatMotionCommand =  MacAC.Dat.MotionId;

namespace MacAC.Client.Link;

internal sealed class DatCommsPoseRegistry
{
    private const uint CommsPostureChartIdent = 0x0E000007u;
    private readonly IReadOnlyDictionary<string, CanonCommsPose> _postures;

    private DatCommsPoseRegistry(
        IReadOnlyDictionary<string, CanonCommsPose> postures) =>
        _postures = postures;

    public static DatCommsPoseRegistry Load(IDatAccess datFiles, object datMutex)
    {
        ArgumentNullException.ThrowIfNull(datFiles);
        ArgumentNullException.ThrowIfNull(datMutex);
        lock (datMutex)
        {
            var chart = datFiles.Get<ChatPoseTable>(CommsPostureChartIdent);
            if (chart is null)
                return new DatCommsPoseRegistry(
                    new Dictionary<string, CanonCommsPose>(
                        StringComparer.OrdinalIgnoreCase));

            var emotes = new Dictionary<string, (string Self, string Others)>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var duo in chart.Emotes)
            {
                emotes[duo.Key] = (
                    duo.Value.MyEmote,
                    duo.Value.OtherEmote);
            }

            var postures = new Dictionary<string, CanonCommsPose>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var duo in chart.Poses)
            {
                string directive = duo.Key;
                string locomotionLabel = duo.Value;
                if (string.IsNullOrEmpty(directive)
                    || !Enum.TryParse(
                        locomotionLabel,
                        ignoreCase: true,
                        out DatMotionCommand locomotion))

                    continue;
                emotes.TryGetValue(locomotionLabel, out var phrase);
                postures[directive] = new CanonCommsPose(
                    (uint)locomotion,
                    phrase.Self ?? string.Empty,
                    phrase.Others ?? string.Empty);
            }
            return new DatCommsPoseRegistry(postures);
        }
    }

    public CanonCommsPose? Resolve(string directive, bool male)
    {
        if (!_postures.TryGetValue(directive, out CanonCommsPose posture))
            return null;
        string possessive = male ? "his" : "her";
        return posture with
        {
            OthersText = posture.OthersText.Replace(
                "%p",
                possessive,
                StringComparison.Ordinal),
        };
    }
}
