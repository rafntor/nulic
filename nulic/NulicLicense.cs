using F23.StringSimilarity;
using Serilog;
using static nulic.LicenseDownload;

namespace nulic;

internal class NulicLicense
{
    public FileInfo Filepath { get; private set; }
    public const string NOASSERTION = "NOASSERTION"; // https://github.com/spdx/spdx-spec/issues/49
    public string SpdxID => _spdx_id ?? NOASSERTION;
    public IEnumerable<string> Copyright { get; private set; } = Enumerable.Empty<string>();
    public readonly Uri? LicenseUrl;
    public Exception? InitException { get; private set; }
    public bool IsNotFound => ReferenceEquals(this, _not_found);

    static readonly FileInfo _null_file = new(OperatingSystem.IsWindows() ? "nul" : "/dev/null");
    // Sentinel returned when the package is not in the local cache — no license, no error logged
    static readonly NulicLicense _not_found = new(_null_file);
    public static NulicLicense NotFound => _not_found;
    // private instance state
    string? _spdx_id;
    readonly object _initLock = new();
    Task? _initTask;
    IDictionary<string, int>? _profile;
    // static registry — dictionaries for O(1) lookup
    static readonly object _lock = new();
    static readonly Dictionary<string, NulicLicense> _byPath = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, NulicLicense> _bySpdxId = new(StringComparer.Ordinal);
    static readonly Dictionary<string, NulicLicense> _byContentHash = new(StringComparer.Ordinal);
    static readonly Cosine _strcmp = new();
    static NulicLicense()
    {
        SeedCommonLicenses();
    }
    static void SeedCommonLicenses()
    {
        foreach (var license in CommonLicenses.Licenses)
        {
            var nl = new NulicLicense(_null_file)
            {
                _spdx_id = license.Key,
                _profile = _strcmp.GetProfile(license.Value),
            };
            _bySpdxId[license.Key] = nl;
        }
    }
    NulicLicense(FileInfo filepath, Uri? url = null)
    {
        LicenseUrl = url;
        Filepath = filepath;

        if (filepath != _null_file)
            Log.Information("creating ({path})", filepath);
    }
    async Task InitializeOnce(Func<Task<string>> text_getter)
    {
        string? license_text = null;
        bool already_on_disk = Filepath.Exists && Filepath.Length > 0;

        if (already_on_disk) // use existing license text from a previous run
        {
            license_text = File.ReadAllText(Filepath.FullName);
        }
        else // lookup and save license text
        {
            if (_spdx_id != null) // may be a standard-license promoted
                CommonLicenses.Licenses.TryGetValue(_spdx_id, out license_text);

            if (license_text is null)
                license_text = await text_getter();
        }

        // compute metadata first (needed for dedup copy and final output)
        _profile = _strcmp.GetProfile(license_text);

        if (_spdx_id is null)
        {
            _spdx_id = LookupSpdxID(_profile) ?? LicenseAnalysis.LookupSpdxIDByKeywords(license_text);
            Copyright = LicenseAnalysis.LookupCopyrights(new StringReader(license_text));
        }

        if (!already_on_disk)
        {
            // dedup: if identical content already exists, point to that file and skip write
            var hash = ComputeContentHash(license_text);
            lock (_lock)
            {
                if (_byContentHash.TryGetValue(hash, out var canonical))
                {
                    Filepath = canonical.Filepath;
                    return;
                }
                _byContentHash[hash] = this;
            }

            Filepath.Directory?.Create();

            using var sw = new StreamWriter(Filepath.OpenWrite());
            await sw.WriteAsync(license_text);
        }
    }
    Task Initialize(Func<Task<string>> text_getter)
    {
        lock (_initLock)
            _initTask ??= RunInitOnce(text_getter);
        return _initTask!;
    }
    async Task RunInitOnce(Func<Task<string>> text_getter)
    {
        try
        {
            await InitializeOnce(text_getter);
        }
        catch (Exception ex)
        {
            InitException = ex;
        }
    }
    public static async Task<NulicLicense> FindOrCreate(Func<Task<string>> text_getter, FileInfo filepath, Uri? url = null, string? spdx_id = null)
    {
        NulicLicense? result;

        lock (_lock)
        {
            if (!_byPath.TryGetValue(filepath.FullName, out result))
            {
                if (spdx_id != null && _bySpdxId.TryGetValue(spdx_id, out result) && result.Filepath == _null_file)
                {
                    // Promote CommonLicense stub to a real file path
                    result.Filepath = filepath;
                    _byPath[filepath.FullName] = result;
                }
                else
                {
                    result = new NulicLicense(filepath, url);
                    if (spdx_id != null)
                    {
                        result._spdx_id = spdx_id;
                        _bySpdxId[spdx_id] = result;
                    }
                    _byPath[filepath.FullName] = result;
                }
            }
        }

        await result.Initialize(text_getter);

        return result;
    }
    static string? LookupSpdxID(IDictionary<string, int> profile)
    {
        lock (_lock)
        {
            foreach (var license in _bySpdxId.Values)
            {
                if (license._profile is null) continue;
                var similarity = _strcmp.Similarity(profile, license._profile);
                if (similarity > 0.9)
                    return license._spdx_id;
            }
        }

        return null;
    }
    static string ComputeContentHash(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }
    internal static void Reset()
    {
        lock (_lock)
        {
            _byPath.Clear();
            _bySpdxId.Clear();
            _byContentHash.Clear();
            SeedCommonLicenses();
        }
    }
}
