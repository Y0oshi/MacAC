using MacAC.Forge;

if (ForgeArgs.WantsHelp(args))
{
    Console.Out.WriteLine(ForgeArgs.Usage);
    return 0;
}

if (ForgeArgs.Read(args, Console.Error) is not { } invocation)
    return 2;

if (!Directory.Exists(invocation.DatDirectory))
{
    Console.Error.WriteLine($"error: directory not found: {invocation.DatDirectory}");
    return 2;
}

IForgeProgressTap? tap = invocation.ProgressJson ? new ForgeProgressJsonScribe(Console.Out) : null;
try
{
    return PakForge.Run(new ForgeJob
    {
        DatDirection = invocation.DatDirectory,
        OutTrail = invocation.OutputPath,
        IdentSift = invocation.IdFilter,
        LbSift = invocation.LandblockFilter,
        Threads = invocation.Threads,
        Progress = tap,
    });
}
catch (Exception miss)
{
    tap?.Error(miss.Message);
    Console.Error.WriteLine($"error: {miss.Message}");
    return 1;
}
