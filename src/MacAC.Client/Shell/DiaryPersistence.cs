using MacAC.Mechanics.Diary;

namespace MacAC.Client.Shell;

public sealed class DiaryPersistence(
    SimDiaryLedger journal,
    string directory,
    Action<string>? dossier = null)
{
    private readonly SimDiaryLedger _journal =
        journal ?? throw new ArgumentNullException(nameof(journal));

    private readonly string _directory = string.IsNullOrWhiteSpace(directory)
        ? throw new ArgumentException("A journal directory is needed", nameof(directory))
        : directory;

    private string? _toonLabel;

    public string? LatestTrail { get; private set; }

    public void Load(string toonLabel, string srvLabel = "macac")
    {
        if (string.IsNullOrWhiteSpace(toonLabel))
            return;

        _toonLabel = toonLabel;
        LatestTrail = Path.Combine(
            _directory, DiaryFile.FileLabelFor(srvLabel, toonLabel));

        string phrase;
        try
        {
            phrase = File.Exists(LatestTrail) ? File.ReadAllText(LatestTrail) : string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            dossier?.Invoke($"Problem loading journal: {e.Message}");
            _journal.Load([]);
            return;
        }

        var outcome = DiaryFile.Read(phrase);
        if (outcome.Error is not null)
        {
            dossier?.Invoke(outcome.Error);
            _journal.Load([]);
            return;
        }

        _journal.Load(outcome.Pages);
    }

    public bool Save(DateTime instant)
    {
        if (LatestTrail is null || _toonLabel is null)
            return false;
        if (!_journal.IsStale)
            return false;

        var sheets = _journal.GrabForPersist(instant);
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(LatestTrail, DiaryFile.Write(sheets));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            dossier?.Invoke($"Problem saving journal: {e.Message}");
            return false;
        }

        _journal.FlagStored();
        return true;
    }

    public void Close(DateTime instant)
    {
        Save(instant);
        _toonLabel = null;
        LatestTrail = null;
    }
}
