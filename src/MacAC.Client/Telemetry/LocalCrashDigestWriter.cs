using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MacAC.Client.Graphics.Gpu.Vulkan;

namespace MacAC.Client.Telemetry;

// One bounded, local, best-effort report at the existing Run failure boundary
internal static class LocalCrashDigestWriter
{
    internal const int UpperDossierOctets = 256 * 1024;
    internal const int UpperExceptionJoints = 8;
    private const string Truncated = "[truncated]";

    internal static string? TryEmit(
        Exception miss,
        string telemetryFolder,
        Func<LocalCrashDigestScope?>? grabCtx = null,
        Action<string>? alert = null)
    {
        string? temporaryTrail = null;
        bool ownsTemporaryFile = false;
        try
        {
            LocalCrashDigestScope? ctx = null;
            bool ctxGrabFailed = false;
            try { ctx = grabCtx?.Invoke(); }
            catch { ctxGrabFailed = true; }

            var utc = DateTimeOffset.UtcNow;
            int procIdent = Environment.ProcessId;
            Assembly assembly = typeof(LocalCrashDigestWriter).Assembly;
            List<FaultListing> exceptions = new List<FaultListing>(UpperExceptionJoints);
            AppendException(miss, null, "root", null, exceptions);
            var gpu = ctx?.Gpu;
            var realm = ctx?.World;
            var dossier = new
            {
                SchemaVersion = 1,
                Utc = utc,
                ProcessId = procIdent,
                AssemblyVersion = Limit(assembly.GetName().Version?.ToString(), 256),
                InformationalVersion = Limit(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion, 512),
                ModuleMvid = assembly.ManifestModule.ModuleVersionId,
                ContextCaptureFailed = ctxGrabFailed,
                Exceptions = exceptions,
                ExceptionTreeTruncated = exceptions.Any(joint => joint.ChildrenTruncated),
                Gpu = gpu is null ? null : new OwnCrashGpu(
                    Limit(gpu.DeviceName, 512), Limit(gpu.DriverInfo, 512),
                    Limit(gpu.InstanceApiVersion, 128), Limit(gpu.DeviceApiVersion, 128),
                    gpu.DeviceApiVersionPacked, gpu.Width, gpu.Height, gpu.SampleCount),
                World = realm is null ? null : new
                {
                    realm.CellId,
                    X = Finite(realm.X),
                    Y = Finite(realm.Y),
                    Z = Finite(realm.Z),
                    State = Limit(realm.State, 128),
                    NonFiniteCoordinates = IsNonFinite(realm.X) || IsNonFinite(realm.Y) || IsNonFinite(realm.Z),
                },
            };

            byte[] octets = JsonSerializer.SerializeToUtf8Bytes(dossier, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            if (octets.Length > UpperDossierOctets)
                throw new InvalidOperationException();

            string persona = $"crash-{utc:yyyyMMddTHHmmssfffffffZ}-{procIdent}-{Guid.NewGuid():N}";
            string finalTrail = Path.Combine(telemetryFolder, persona + ".json");
            temporaryTrail = Path.Combine(telemetryFolder, "." + persona + $"-{Guid.NewGuid():N}.tmp");
            Directory.CreateDirectory(telemetryFolder);
            using (var flow = new FileStream(temporaryTrail, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporaryFile = true;
                flow.Write(octets);
                flow.Flush();
            }
            File.Move(temporaryTrail, finalTrail, overwrite: false);
            ownsTemporaryFile = false;
            Alert(alert, $"Local crash report saved: {finalTrail}");
            return finalTrail;
        }
        catch
        {
            Alert(alert, "Local crash report unavailable.");
            return null;
        }
        finally
        {
            if (ownsTemporaryFile && temporaryTrail is not null)
            {
                try { File.Delete(temporaryTrail); }
                catch { }
            }
        }
    }

    private static void AppendException(
        Exception exception, int? ancestorOrdinal, string relation, int? aggregateOrdinal,
        List<FaultListing> listings)
    {
        int ordinal = listings.Count;
        FaultListing listing = new FaultListing(
            ancestorOrdinal, relation, aggregateOrdinal,
            Limit(exception.GetType().FullName, 512), exception.HResult, MethodPile(exception),
            exception is VkCallException vk
                ? new VkEntry(Limit(vk.Operation, 512), (int)vk.Result, Limit(vk.Result.ToString(), 128))
                : null);
        listings.Add(listing);
        if (exception is AggregateException aggregate)
        {
            for (int idx = 0; idx < aggregate.InnerExceptions.Count; ++idx)
            {
                if (listings.Count == UpperExceptionJoints)
                {
                    listing.ChildrenTruncated = true;
                    break;
                }
                AppendException(aggregate.InnerExceptions[idx], ordinal, "aggregate", idx, listings);
            }
        }
        else if (exception.InnerException is { } interior)
        {
            if (listings.Count == UpperExceptionJoints)
                listing.ChildrenTruncated = true;
            else
                AppendException(interior, ordinal, "inner", null, listings);
        }
    }

    private static string MethodPile(Exception exception)
    {
        try
        {
            StackTrace trace = new StackTrace(exception, fNeedFileInfo: false);
            StringBuilder phrase = new StringBuilder();
            for (int idx = 0; idx < trace.FrameCount && idx < 48; ++idx)
            {
                MethodBase? method = trace.GetFrame(idx)?.GetMethod();
                if (method is null)
                    continue;
                if (phrase.Length is not 0)
                    phrase.Append('\n');
                phrase.Append(Limit(method.DeclaringType?.FullName, 256));
                phrase.Append('.');
                phrase.Append(Limit(method.Name, 256));
                if (phrase.Length > 3072)
                    return Limit(phrase.ToString(), 3072)!;
            }
            if (trace.FrameCount > 48)
                phrase.Append(Truncated);
            return Limit(phrase.ToString(), 3072)!;
        }
        catch { return "[unavailable]"; }
    }

    private static string? Limit(string? val, int threshold)
    {
        if (val is null || val.Length <= threshold)
            return val;
        int finish = threshold - Truncated.Length;
        if (finish > 0 && char.IsHighSurrogate(val[finish - 1]) && char.IsLowSurrogate(val[finish]))
            --finish;
        return string.Concat(val.AsSpan(0, finish), Truncated);
    }

    private static bool IsNonFinite(float? val) => val.HasValue && !float.IsFinite(val.Value);
    private static float? Finite(float? val) => IsNonFinite(val) ? null : val;

    private static void Alert(Action<string>? alert, string phrase)
    {
        try { alert?.Invoke(phrase); }
        catch { }
    }

    private sealed record VkEntry(string? Operation, int Result, string? ResultName);

    private sealed record FaultListing(
        int? ParentIndex, string Relation, int? AggregateIndex, string? Type,
        int HResult, string Stack, VkEntry? Vulkan)
    {
        public bool ChildrenTruncated { get; set; }
    }
}

internal sealed record LocalCrashDigestScope(OwnCrashGpu? Gpu, LocalCrashRealm? World);

internal sealed record OwnCrashGpu(
    string? DeviceName, string? DriverInfo, string? InstanceApiVersion,
    string? DeviceApiVersion, uint? DeviceApiVersionPacked,
    uint? Width, uint? Height, int? SampleCount);

internal sealed record LocalCrashRealm(uint? CellId, float? X, float? Y, float? Z, string? State);
