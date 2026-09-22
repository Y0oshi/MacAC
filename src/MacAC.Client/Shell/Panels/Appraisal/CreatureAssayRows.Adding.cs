using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Shell;

namespace MacAC.Client.Shell.Panels;

public static partial class CreatureAssayRows
{
    private static void AppendSocietyRank(
        List<CreatureAssayRow> ranks,
        TraitBundle props,
        int ownFactionBitset)
    {
        if (!props.Ints.TryGetValue(Faction1BitsetProp, out int markBitset))
            return;

        string label;
        int grade;
        int markBit;
        if ((markBitset & CelestialHandBit) is not 0)
        {
            label = "Celestial Hand";
            grade = Get(props, SocietyGradeCelestialHandProp);
            markBit = CelestialHandBit;
        }
        else if ((markBitset & EldrytchWebBit) is not 0)
        {
            label = "Eldrytch Web";
            grade = Get(props, SocietyGradeEldrytchWebProp);
            markBit = EldrytchWebBit;
        }
        else if ((markBitset & RadiantBloodBit) is 0)
        {
            ranks.Add(new CreatureAssayRow(
                "Society:", Unknown, CreatureAssayValueStyle.Normal));
            return;
        }
        else
        {
            label = "Radiant Blood";
            grade = Get(props, SocietyGradeRadiantBloodProp);
            markBit = RadiantBloodBit;
        }

        ranks.Add(new CreatureAssayRow(
            "Society:",
            label + SocietyGradeSuffix(grade),
            SocietyTint(markBit, ownFactionBitset)));
    }

    private static void AppendAllegianceCascade(
        List<CreatureAssayRow> ranks, TraitBundle props)
    {
        if (!props.Texts.TryGetValue(
                MonarchsBannerProp, out string? monarchsBanner))
        {
            int followers = Get(props, AllegianceFollowersProp);
            if (followers < 0)
                followers = 0;
            string unit = followers is 1 ? "Follower" : "Followers";
            ranks.Add(new CreatureAssayRow(
                "Alleg. Monarch:",
                $"{Number(followers)} {unit}",
                CreatureAssayValueStyle.Normal));
            return;
        }

        if (!props.Texts.TryGetValue(
                PatronsBannerProp, out string? patronsBanner))
        {
            ranks.Add(new CreatureAssayRow(
                "Monarch:", monarchsBanner, CreatureAssayValueStyle.Normal));
            return;
        }

        if (string.Equals(monarchsBanner, patronsBanner, StringComparison.Ordinal))
        {
            ranks.Add(new CreatureAssayRow(
                "Monarch/Patron:",
                monarchsBanner,
                CreatureAssayValueStyle.Normal));
            return;
        }

        ranks.Add(new CreatureAssayRow(
            "Monarch:", monarchsBanner, CreatureAssayValueStyle.Normal));
        ranks.Add(new CreatureAssayRow(
            "Patron:", patronsBanner, CreatureAssayValueStyle.Normal));
    }

    private static void AppendConfigurableExtras(
        List<CreatureAssayRow> ranks, TraitBundle props)
    {
        if (props.Texts.TryGetValue(
                FellowshipProp, out string? fellowship))
        {
            ranks.Add(new CreatureAssayRow(
                "Fellowship:", fellowship, CreatureAssayValueStyle.Normal));
        }

        if (props.Texts.TryGetValue(
                DateOfBirthProp, out string? arrived))
        {
            ranks.Add(new CreatureAssayRow(
                "Arrived in Dereth:",
                arrived,
                CreatureAssayValueStyle.Normal));
        }

        if (props.Ints.TryGetValue(AgeProp, out int ageSecs))
        {
            ranks.Add(new CreatureAssayRow(
                "Time in Dereth:",
                CanonDurationText.Format(ageSecs),
                CreatureAssayValueStyle.Normal));
        }

        if (props.Ints.TryGetValue(ChessGradeProp, out int chessGrade))
        {
            ranks.Add(new CreatureAssayRow(
                "Chess Rank:", Number(chessGrade), CreatureAssayValueStyle.Normal));
        }

        AppendConfigurableExtrasRest(props, ranks);
    }

    private static void AppendConfigurableExtrasRest(TraitBundle props, List<CreatureAssayRow> ranks)
    {
        if (props.Ints.TryGetValue(FishingAptitudeProp, out int fishingAptitude))
        {
            ranks.Add(new CreatureAssayRow(
                "Fishing Skill:",
                Number(fishingAptitude),
                CreatureAssayValueStyle.Normal));
        }
        if (props.Ints.TryGetValue(CountDeathsProp, out int deaths))
        {
            ranks.Add(new CreatureAssayRow(
                "Deaths:",
                deaths <= 0 ? "Has never died" : Number(deaths),
                CreatureAssayValueStyle.Normal));
        }
        if (props.Ints.TryGetValue(
                        CountToonBannersProp, out int banners))
        {
            ranks.Add(new CreatureAssayRow(
                "Titles Earned:", Number(banners), CreatureAssayValueStyle.Normal));
        }
    }
}
