namespace MacAC.Extensibility.RenderPacks.Spirv;

/// <summary>The shader stage a declaration assigns to one SPIR-V asset.</summary>
public enum ShaderStage
{
    Vertex,
    Fragment,
}

/// <summary>Hardware-independent verdict on one declared SPIR-V module.</summary>
public readonly record struct SpirvVerdict(bool Success, string? Reason)
{
    public static SpirvVerdict Valid() => new(true, null);

    public static SpirvVerdict Invalid(string cause) => new(false, cause);
}

public static class SpirvGate
{
    private const uint VertModel = 0;
    private const uint FragmentModel = 4;

    public static SpirvVerdict VetPassShader(
        ReadOnlySpan<byte> spirv,
        ShaderStage juncture,
        RenderPassSpec pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        return Examine(spirv, juncture, BindingPolicy.ForPass(pass));
    }

    public static SpirvVerdict VetPipeVariantShader(
        ReadOnlySpan<byte> spirv,
        ShaderStage juncture,
        PipelineVariantSpec variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return Examine(spirv, juncture, BindingPolicy.ForVariant(variant));
    }

    private static SpirvVerdict Examine(ReadOnlySpan<byte> octets, ShaderStage juncture, BindingPolicy rule)
    {
        SpirvImage? image = SpirvImage.TryUnpack(octets, out string? unpackMiss);
        if (image is null)
            return SpirvVerdict.Invalid(unpackMiss!);

        string? miss = VerifyEntryPoint(image, juncture)
            ?? VerifyDescriptors(image, rule)
            ?? (image.WritesImages ? "storage image writes are forbidden by render-pack API v1" : null);
        return miss is null ? SpirvVerdict.Valid() : SpirvVerdict.Invalid(miss);
    }

    private static string? VerifyEntryPoint(SpirvImage image, ShaderStage juncture)
    {
        uint wanted = juncture == ShaderStage.Vertex ? VertModel : FragmentModel;
        bool single = image.EntryPoints.Count is 1
            && image.EntryPoints[0].ExecutionModel == wanted
            && string.Equals(image.EntryPoints[0].Name, "main", StringComparison.Ordinal);
        return single
            ? null
            : $"the declared {juncture.ToString().ToLowerInvariant()} asset must expose exactly "
                + "entry point 'main' for that stage";
    }

    private static string? VerifyDescriptors(SpirvImage image, BindingPolicy rule)
    {
        var observed = new HashSet<(uint Set, uint Binding)>();
        bool sawPushChunk = false;

        foreach (SpirvVariable variable in image.Variables)
        {
            if (variable.StorageClass == SpirvStorageClass.PushConstant)
            {
                if (sawPushChunk)
                    return "the module declares more than one push-constant block";
                sawPushChunk = true;
                if (AbiBlockRules.VerifyPushChunk(image, variable) is { } pushMiss)
                    return pushMiss;
                continue;
            }

            if (variable.StorageClass is not (SpirvStorageClass.UniformConstant
                or SpirvStorageClass.Uniform
                or SpirvStorageClass.StorageBuffer))

                continue;

            if (!image.TryDescriptorSocket(variable.Id, out uint set, out uint mapping))
                return $"descriptor %{variable.Id} does not declare both set and binding";
            if (!observed.Add((set, mapping)))
                return $"descriptor set {set} binding {mapping} is declared more than once";

            if (VerifySocket(image, rule, variable, set, mapping) is { } socketMiss)
                return socketMiss;
        }

        return null;
    }

    private static string? VerifySocket(
        SpirvImage image,
        BindingPolicy rule,
        SpirvVariable variable,
        uint set,
        uint mapping)
    {
        if (set == ShaderAbi.SampledTextureDescriptorSet && mapping == ShaderAbi.SampledTextureMapping)
        {
            if (!rule.SampledTable)
                return "set 2 binding 0 is sampled without a declared semantic/resource input";
            return image.IsSampledTextureChart(variable)
                ? null
                : "set 2 binding 0 must be one runtime array of combined 2-D-array samplers";
        }

        if (set == ShaderAbi.UniformDescriptorSet)
        {
            if (variable.StorageClass != SpirvStorageClass.Uniform)
                return $"set 3 binding {mapping} must be a uniform buffer";
            if (!rule.PermitsUniform(mapping))
                return $"set 3 binding {mapping} is not declared for this shader role";
            return AbiBlockRules.VerifyUniformChunk(image, variable, mapping);
        }

        if (set is 0 && variable.StorageClass == SpirvStorageClass.StorageBuffer)
        {
            if (!rule.StorageBindings.Contains(mapping))
                return $"set 0 binding {mapping} storage access is not allowed for this shader role";
            if (!image.IsScanSoleDepot(variable))
                return $"set 0 binding {mapping} is writable; render-pack storage writes are forbidden";
            return image.PtsAtSingleChunk(variable)
                ? null
                : $"set 0 binding {mapping} must be one storage-buffer descriptor";
        }

        if (set is 1 && variable.StorageClass == SpirvStorageClass.Uniform)
        {
            if (!rule.UniformBindings.Contains(mapping))
                return $"set 1 binding {mapping} aliases renderer state not allowed for this shader role";
            return image.PtsAtSingleChunk(variable)
                ? null
                : $"set 1 binding {mapping} must be one uniform-buffer descriptor";
        }

        return $"descriptor set {set} binding {mapping} has no render-pack API v1 binding";
    }
}
