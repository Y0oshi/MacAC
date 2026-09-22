namespace MacAC.Client.Extensions;

internal static class ClientVtankProfilesDefault
{
    internal static string Resolve(string blobFolder) =>
        Path.Combine(blobFolder, "vtank");
}
