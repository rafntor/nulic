using F23.StringSimilarity;
using Serilog;
using System.Diagnostics;

namespace nulic;

internal class NulicLicense
{
    public FileInfo Filepath { get; private set; }
    public string SpdxID => _spdx_id ?? "NOASSERTION"; // https://github.com/spdx/spdx-spec/issues/49
    public IEnumerable<string> Copyright { get; private set; } = Enumerable.Empty<string>();
    public readonly Uri? LicenseUrl;
    // private stuff
    string? _spdx_id;
    int _initialized = 0;
    SemaphoreSlim _init_sem = new(0);
    IDictionary<string, int>? _profile;
    static List<NulicLicense> _licenses = new();
    static Cosine _strcmp = new Cosine();
    static readonly FileInfo _null_file = new (OperatingSystem.IsWindows() ? "nul" : "/dev/null");
    static NulicLicense()
    {
        foreach (var license in CommonLicenses.Licenses)
        {
            new NulicLicense(_null_file)
            {
                _spdx_id = license.Key,
                _profile = _strcmp.GetProfile(license.Value),
            };
        }
    }
    NulicLicense(FileInfo filepath, Uri? url = null)
    {
        LicenseUrl = url;
        Filepath = filepath;
        lock (_licenses)
            _licenses.Add(this);
        Log.Information($"created nulic ({filepath})");
    }
    async Task InitializeOnce(Func<Task<string>> text_getter)
    {
        string? license_text = null;

        if (Filepath.Exists && Filepath.Length > 0) // use existing license text
        {
            license_text = File.ReadAllText(Filepath.FullName);
        }
        else // lookup and save license text
        {
            if (_spdx_id != null) // may be a standard-license promoted
                CommonLicenses.Licenses.TryGetValue(_spdx_id, out license_text);

            if (license_text is null)
                license_text = await text_getter();

            using var sw = new StreamWriter(Filepath.OpenWrite());

            await sw.WriteAsync(license_text);
        }

        // now some additional discovery based on license content

        _profile = _strcmp.GetProfile(license_text);

        if (_spdx_id is null)
        {
            _spdx_id = LookupSpdxID(_profile);

            Copyright = LookupCopyrights(new StringReader(license_text));
        }
    }
    async Task Initialize(Func<Task<string>> text_getter)
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
        {
            try
            {
                await InitializeOnce(text_getter);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, $"init license failed ({Filepath})");

                _initialized = -1;
            }

            _init_sem.Release(int.MaxValue);
        }

        await _init_sem.WaitAsync();

        if (_initialized < 0)
            throw new Exception($"init license failed ({Filepath})");
    }
    public static async Task<NulicLicense> FindOrCreate(Func<Task<string>> text_getter, FileInfo filepath, Uri? url = null, string? spdx_id = null)
    {
        NulicLicense? result = null;

        lock (_licenses)
        {
            result = FindExisting(filepath);

            if (result is null)
                result = PromoteExisting(spdx_id, filepath);

            if (result is null)
                result = new NulicLicense(filepath, url); // from now can be found by other tasks!
        }

        await result.Initialize(text_getter);

        return result;
    }
    static string? LookupSpdxID(IDictionary<string, int> profile)
    {
        lock (_licenses)
        {
            foreach (var license in _licenses.Where(l => l._profile != null && l._spdx_id != null))
            {
                var similarity = _strcmp.Similarity(profile, license._profile);

                if (similarity > 0.9)
                    return license._spdx_id;
            }
        }

        return null;
    }
    static NulicLicense? FindExisting(FileInfo filepath)
    {
        var license = _licenses.Find(l => l.Filepath.FullName == filepath.FullName);

        return license;
    }
    static NulicLicense? PromoteExisting(string? spdx_id, FileInfo filepath)
    {
        var license = _licenses.Find(l => l.SpdxID == spdx_id);

        if (license != null)
        {
            Debug.Assert(license.Filepath == _null_file); // promote only once!

            license.Filepath = filepath; // from here on it may be found by findexisting ; also by other tasks
        }

        return license;
    }
    static IEnumerable<string> LookupCopyrights(TextReader license_text)
    {
        List<string> result = new();

        while (license_text.ReadLine() is string line)
        {
            var idx = line.IndexOf("copyright (c)", StringComparison.OrdinalIgnoreCase);

            if (idx < 0)
                idx = line.IndexOf("copyright ©", StringComparison.OrdinalIgnoreCase);

            if (idx < 0)
                continue;

            result.Add(line.Substring(idx));
        }

        return result;
    }
}
