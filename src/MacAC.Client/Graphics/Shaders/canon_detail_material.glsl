struct RetailDetailMaterialResult {
    vec3 rgb;
    float alpha;
};

RetailDetailMaterialResult macacRetailDetailMaterial(
    vec3 baseRgb,
    vec3 diffuseRgb,
    vec4 detail,
    float materialAlpha)
{
    float wgt = materialAlpha * detail.a;
    return RetailDetailMaterialResult(
        detail.rgb * wgt + (baseRgb * diffuseRgb) * (1.0 - wgt),
        materialAlpha * detail.a * detail.a);
}
