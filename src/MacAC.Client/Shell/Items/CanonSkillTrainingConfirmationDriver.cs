using System.Globalization;
using MacAC.Client.Shell.Panels;

namespace MacAC.Client.Shell;

public sealed class CanonSkillTrainingConfirmationDriver(CanonPromptMint dialogs)
{
    public const string MsgFmt =
        "Are you sure you want to spend {0} credits to train {1}?";

    private readonly CanonPromptMint _popups = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

    public uint Request(
        ToonStatDriver.RaiseAsk request,
        ToonSheet sheet,
        Action<ToonStatDriver.RaiseAsk> approved,
        Action finished)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(approved);
        ArgumentNullException.ThrowIfNull(finished);

        if (request.Kind != ToonStatDriver.EmitMarkFlavor.TrainSkill)
            throw new ArgumentException("Only TrainSkill requests require this confirmation", nameof(request));
        if (request.Cost is <= 0 or > uint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(request), "Training cost must fit retail's uint32 field");

        ToonSkill aptitude = sheet.Skills.FirstOrDefault(contender => contender.Id == request.StatId)
            ?? throw new InvalidOperationException($"Skill {request.StatId} is absent from the character sheet");

        string msg = string.Format(
            CultureInfo.InvariantCulture,
            MsgFmt,
            request.Cost,
            aptitude.Name);

        var blob = CanonPromptData.Confirmation(msg)
            .Set(CanonPromptProperty.TrainAptitudeIdent, request.StatId)
            .Set(CanonPromptProperty.TrainAptitudeCredits, checked((uint)request.Cost));

        return _popups.MakeDialog(blob, outcome =>
        {
            if (outcome.FetchBoolean(CanonPromptProperty.AckOutcome))
            {
                uint aptitudeIdent = outcome.FetchUInt32(CanonPromptProperty.TrainAptitudeIdent);
                uint credits = outcome.FetchUInt32(CanonPromptProperty.TrainAptitudeCredits);
                if (aptitudeIdent is not 0u && credits is not 0u)
                {
                    approved(new ToonStatDriver.RaiseAsk(
                        ToonStatDriver.EmitMarkFlavor.TrainSkill,
                        aptitudeIdent,
                        credits,
                        Amount: 1));
                }
            }

            finished();
        });
    }
}
