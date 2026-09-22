namespace MacAC.Mechanics.Arcana;

public static class EnchantmentLedgerView
{
    public static IReadOnlyList<LiveEnchantmentRow> FetchEnchantmentsInFx(IEnumerable<LiveEnchantmentRow> enchantments)
    {
        ArgumentNullException.ThrowIfNull(enchantments);
        LiveEnchantmentRow[] all = enchantments as LiveEnchantmentRow[] ?? [.. enchantments];
        var winners = new List<LiveEnchantmentRow>();
        Duel(all, 1u, winners);
        Duel(all, 2u, winners);
        return winners;
    }

    private static void Duel(LiveEnchantmentRow[] all, uint bin, List<LiveEnchantmentRow> winners)
    {
        foreach (LiveEnchantmentRow challenger in all)
        {
            if (challenger.Bucket != bin)
                continue;

            int seat = winners.FindIndex(w => w.SpellCategory == challenger.SpellCategory);
            if (seat >= 0)
            {
                if (!Beats(challenger, winners[seat]))
                    continue;
                winners.RemoveAt(seat);
            }
            winners.Add(challenger);
        }
    }

    private static bool Beats(in LiveEnchantmentRow challenger, in LiveEnchantmentRow holder)
    {
        int theirs = unchecked((int)challenger.PowerLevel);
        int ours = unchecked((int)holder.PowerLevel);
        return theirs > ours || (theirs == ours && challenger.StartTime > holder.StartTime);
    }
}
