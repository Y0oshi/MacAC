using System.Collections.Concurrent;

namespace MacAC.Forge;

// The end-of-run console report
internal static class ForgeSummary
{
    private const int FlawsShown = 200;

    public static void Dump(ForgeTally count, string productTrail)
    {
        Console.WriteLine();
        Console.WriteLine("=== bake summary ===");
        Console.WriteLine($"  GfxObj keys:      {count.GfxObjRefTags:N0}");
        Console.WriteLine($"  Setup keys:       {count.RigTags:N0}");
        DumpRest(count, productTrail);
    }

    private static void DumpRest(ForgeTally count, string productTrail)
    {
        Console.WriteLine($"  EnvCell keys:     {count.EnvironChamberTags:N0}");
        Console.WriteLine($"  EnvCell unique:   {count.UniqueEnvironChamberGeometries:N0}");
        Console.WriteLine($"  EnvCell aliases:  {count.EnvironChamberAliases:N0}");
        DumpTail(count, productTrail);
    }

    private static void DumpTail(ForgeTally count, string productTrail)
    {
        Console.WriteLine($"  EnvCell dedup:    {count.EnvironChamberDedupRatio:F1}x");
        Console.WriteLine(
                    $"  Gfx collisions:   {count.GfxObjRefImpactTags:N0} keys, "
                    + $"{count.UniqueGfxObjRefImpacts:N0} blobs, "
                    + $"{count.GfxObjRefImpactAliases:N0} aliases");
        Console.WriteLine(
                    $"  Setup collisions: {count.RigImpactTags:N0} keys, "
                    + $"{count.UniqueRigImpacts:N0} blobs, "
                    + $"{count.RigImpactAliases:N0} aliases");
        DumpCoda(count, productTrail);
    }

    private static void DumpCoda(ForgeTally count, string productTrail)
    {
        Console.WriteLine(
                        $"  Cell structures:  {count.ChamberStructureImpactTags:N0} keys, "
                        + $"{count.UniqueChamberStructureImpacts:N0} blobs, "
                        + $"{count.ChamberStructureImpactAliases:N0} aliases");
        Console.WriteLine($"  Cell topologies:   {count.EnvironChamberWiringTags:N0}");
        DumpCoda2(count, productTrail);
    }

    private static void DumpCoda2(ForgeTally count, string productTrail)
    {
        Console.WriteLine($"  texture payloads:  {count.TextureCargoTags:N0}");
        Console.WriteLine(
                    $"  side-staged keys: {count.FlankLinedTags:N0} "
                    + $"({count.FlankLinedDuplicateTags:N0} duplicate keys)");
        Console.WriteLine($"  physical blobs:   {count.PhysicalBlobs:N0}");
        Console.WriteLine($"  total keys:       {count.SumTags:N0}");
        DumpCoda3(count, productTrail);
    }

    private static void DumpCoda3(ForgeTally count, string productTrail)
    {
        Console.WriteLine(
                        $"  compressed blobs: {count.CompressedBlobs:N0}; "
                        + $"payload {Mb(count.DecodedCargoBytes):F1} -> "
                        + $"{Mb(count.StoredCargoBytes):F1} MB "
                        + $"({count.CargoCompressionRatio:F2}x)");
        Console.WriteLine($"  failures:         {count.Failures:N0}");
        DumpCoda4(count, productTrail);
    }

    private static void DumpCoda4(ForgeTally count, string productTrail)
    {
        Console.WriteLine($"  extract + write:  {count.ExtractionAndEmitPassed.TotalSeconds:F1} s");
        Console.WriteLine($"  total validated:  {count.Passed.TotalSeconds:F1} s");
        Console.WriteLine($"  peak working set: {Mb(count.PeakWorkingSetOctets):F1} MB");
        FinishWriteLine5(count, productTrail);
    }

    private static void FinishWriteLine5(ForgeTally count, string productTrail)
    {
        Console.WriteLine($"  peak private:     {Mb(count.PeakPrivateOctets):F1} MB");
        Console.WriteLine($"  output size:      {Mb(count.ProductOctets):F1} MB");
        Console.WriteLine($"  output path:      {productTrail}");
    }

    public static void DumpFlaws(ConcurrentBag<ForgeRun.Fault> flaws)
    {
        if (flaws.IsEmpty)
            return;

        Console.WriteLine();
        Console.WriteLine($"failures ({flaws.Count}):");
        foreach (var (kind, fileIdent, cause) in flaws
                     .OrderBy(static fault => fault.Type)
                     .ThenBy(static fault => fault.FileId)
                     .Take(FlawsShown))
        {
            Console.WriteLine($"  {kind,-12} 0x{fileIdent:X8}: {cause}");
        }

        if (flaws.Count > FlawsShown)
            Console.WriteLine($"  ... and {flaws.Count - FlawsShown} more");
    }

    private static double Mb(long octets) => octets / 1024.0 / 1024.0;
}
