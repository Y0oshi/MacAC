using System.Text;

namespace MacAC.Tools.ShaderCompiler;

internal static class VulkanGlslPreamble
{
    internal static IReadOnlyList<string> PushConstantFields { get; } =
    [
        "uViewProjection",
        "uDrawIDOffset",
        "uLightingMode",
        "uRenderPass",
        "uLightDebug",
        "uTextureIndexA",
        "uTextureIndexB",
        "uParamA",
        "uParamB",
        "uTextureIndexC",
        "uTextureIndexD",
    ];

    // Builds the text inserted after the #version directive
    internal static string Build(string stage) =>
        Build(stage, includePackUniformSet: false, includePackTextureIndices: false);

    private static string Build(
        string stage,
        bool includePackUniformSet,
        bool includePackTextureIndices)
    {
        var text = new StringBuilder();
        text.AppendLine("// injected by the shader compiler");
        text.AppendLine("// Vulkan descriptor bindings and shader input compatibility.");
        text.AppendLine("#extension GL_EXT_nonuniform_qualifier : require");
        if (stage == "vert")
        {
            // gl_DrawID is Vulkan's shaderDrawParameters feature, and glslang still gates the identifier
            // behind the ARB extension name even when targeting Vulkan.
            text.AppendLine("#extension GL_ARB_shader_draw_parameters : require");
        }

        text.AppendLine();
        text.AppendLine("// Set 1: uniform buffers.");
        text.AppendLine("#undef MACAC_UBO_SET");
        text.AppendLine("#define MACAC_UBO_SET set = 1,");
        if (includePackUniformSet)
        {
            text.AppendLine("#undef MACAC_PACK_UBO_SET");
            text.AppendLine("#define MACAC_PACK_UBO_SET set = 3,");
        }
        text.AppendLine();
        text.AppendLine("// Set 2: the global sampled-texture table that replaces");
        text.AppendLine("// GL_ARB_bindless_texture. Variable count, partially bound,");
        text.AppendLine("// update-after-bind; the CPU never writes it per frame.");
        text.AppendLine("layout(set = 2, binding = 0) uniform sampler2DArray uTextures[];");
        text.AppendLine("#undef MACAC_TEXTURE_HANDLE");
        text.AppendLine("#define MACAC_TEXTURE_HANDLE(idx) (idx)");
        text.AppendLine("#define MACAC_TEXTURE(idx) uTextures[nonuniformEXT(uint(idx))]");
        text.AppendLine(
            "#define MACAC_SAMPLE_2D(idx, uv) texture(MACAC_TEXTURE(idx), vec3((uv), 0.0))");
        text.AppendLine(
            "#define MACAC_SAMPLE_ARRAY(idx, uvw) texture(MACAC_TEXTURE(idx), uvw)");
        text.AppendLine("#define MACAC_TEXTURE_NONE 0xFFFFFFFFu");
        text.AppendLine();
        text.AppendLine("// Push constants: one shared 96-byte block, so switching pipelines");
        text.AppendLine("// mid-pass invalidates neither descriptors nor constants.");
        text.AppendLine("layout(push_constant) uniform EnginePushBlock {");
        text.AppendLine("    mat4 viewProjection;");
        text.AppendLine("    int drawIdOffset;");
        text.AppendLine("    int lightingMode;");
        text.AppendLine("    int renderPass;");
        text.AppendLine("    int lightDebug;");
        text.AppendLine("    uint textureIndexA;");
        text.AppendLine("    uint textureIndexB;");
        text.AppendLine("    float paramA;");
        text.AppendLine("    float paramB;");
        text.AppendLine("} macacPush;");
        text.AppendLine();
        text.AppendLine("#define uViewProjection macacPush.viewProjection");
        text.AppendLine("#define uDrawIDOffset   macacPush.drawIdOffset");
        text.AppendLine("#define uLightingMode   macacPush.lightingMode");
        text.AppendLine("#define uRenderPass     macacPush.renderPass");
        text.AppendLine("#define uLightDebug     macacPush.lightDebug");
        text.AppendLine("#define uTextureIndexA  macacPush.textureIndexA");
        text.AppendLine("#define uTextureIndexB  macacPush.textureIndexB");
        text.AppendLine("#define uParamA         macacPush.paramA");
        text.AppendLine("#define uParamB         macacPush.paramB");
        if (includePackTextureIndices)
        {
            text.AppendLine("#define uTextureIndexC  floatBitsToUint(macacPush.paramA)");
            text.AppendLine("#define uTextureIndexD  floatBitsToUint(macacPush.paramB)");
        }
        text.AppendLine();
        text.AppendLine("// gl_DrawIDARB stays as written — glslang exposes it for Vulkan");
        text.AppendLine("// under the same ARB extension name. gl_InstanceIndex already includes");
        text.AppendLine("// firstInstance, so the GL idiom gl_BaseInstanceARB + gl_InstanceID");
        text.AppendLine("// collapses to it exactly.");
        text.AppendLine(stage == "vert"
            ? "#define gl_BaseInstanceARB 0\n"
                + "#define gl_InstanceID gl_InstanceIndex\n"
                + "#define gl_VertexID gl_VertexIndex"
            : "// (the vertex/instance-index rewrites apply to the vertex stage only)");
        text.AppendLine("// ---- end injected preamble ----");
        return text.ToString();
    }

    internal static string Apply(string source, string stage)
    {
        ArgumentNullException.ThrowIfNull(source);
        bool includePackUniformSet = source.Contains("MACAC_PACK_UBO_SET", StringComparison.Ordinal);
        bool includePackTextureIndices =
            source.Contains("uTextureIndexC", StringComparison.Ordinal)
            || source.Contains("uTextureIndexD", StringComparison.Ordinal);
        string[] lines = source.Replace("\r\n", "\n").Split('\n');
        var output = new StringBuilder();
        bool injected = false;

        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();
            if (!injected && trimmed.StartsWith("#version", StringComparison.Ordinal))
            {
                output.AppendLine(HighestVersion(trimmed) >= 460 ? "#version 460 core" : "#version 450 core");
                output.Append(Build(stage, includePackUniformSet, includePackTextureIndices));
                injected = true;
                continue;
            }

            // Bindless textures are what the set-2 descriptor array replaces;
            // requiring the extension under Vulkan is an error rather than a
            // no-op. (GL_ARB_shader_draw_parameters is kept - glslang still gates
            // gl_DrawID behind that name when targeting Vulkan.)
            if (trimmed.StartsWith("#extension GL_ARB_bindless_texture", StringComparison.Ordinal))
            {
                output.AppendLine($"// (dropped for Vulkan: {trimmed})");
                continue;
            }

            if (IsDefaultBlockUniformDeclaration(trimmed))
            {
                output.AppendLine($"// (declaration dropped for Vulkan: {trimmed})");
                continue;
            }

            output.AppendLine(line);
        }

        if (!injected)
        {
            throw new InvalidOperationException(
                "The shader has no #version directive, so there is nowhere to inject the Vulkan preamble");
        }

        return output.ToString();
    }

    internal static int HighestVersion(string versionDirective)
    {
        string[] parts = versionDirective.Split(
            [' ', '\t'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 && int.TryParse(parts[1], out int version) ? version : 450;
    }

    internal static bool IsDefaultBlockUniformDeclaration(string trimmedLine)
    {
        ArgumentNullException.ThrowIfNull(trimmedLine);
        if (!trimmedLine.StartsWith("uniform ", StringComparison.Ordinal))
            return false;

        int comment = trimmedLine.IndexOf("//", StringComparison.Ordinal);
        string code = comment >= 0 ? trimmedLine[..comment] : trimmedLine;
        return !code.Contains('{') && code.TrimEnd().EndsWith(';');
    }
}
