using Microsoft.Build.Construction;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;
using F23.StringSimilarity;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace nulic;

internal class NulicLicense
{
    public FileInfo Filepath { get; private set; }
    public readonly string SpdxID; // spdx-license-id
    public readonly Uri? LicenseUrl;
    readonly IDictionary<string, int> _profile;
    static List<NulicLicense> _licenses = new();
    static ShingleBased _strcmp = new SorensenDice();
    static readonly FileInfo _null_file = new (OperatingSystem.IsWindows() ? "nul" : "/dev/null");
    static NulicLicense()
    {
        foreach (var license in CommonLicenses.Licenses)
        {
            var profile = _strcmp.GetProfile(license.Value);

            new NulicLicense(_null_file, profile, license.Key, null);
        }
    }
    NulicLicense(FileInfo filepath, IDictionary<string, int> profile, string spdx_id, Uri? url)
    {
        _profile = profile;
        Filepath = filepath;
        SpdxID = spdx_id;
        LicenseUrl = url;
        _licenses.Add(this);

        Log.Information($"created nulic {filepath}");
    }
    public static async Task<NulicLicense> Create(FileInfo filepath, Stream? stream = null, Uri? url = null, string? spdx_id = null)
    {
        if (FindExisting(filepath) is NulicLicense license)
            return license;

        if (PromoteExisting(spdx_id, filepath) is NulicLicense promoted)
            return promoted;

        using var reader = stream != null ? new StreamReader(stream) : new StreamReader(filepath.FullName);
        var license_text = await reader.ReadToEndAsync();
        var profile = _strcmp.GetProfile(license_text);

        if (spdx_id is null)
            spdx_id = "NOASSERTION";  // https://github.com/spdx/spdx-spec/issues/49

        return new NulicLicense(filepath, profile, spdx_id, url);
    }
    public static NulicLicense? FindExisting(FileInfo filepath)
    {
        return _licenses.Find(l => l.Filepath.FullName == filepath.FullName);
    }
    static NulicLicense? PromoteExisting(string? spdx_id, FileInfo filepath)
    {
        var license = _licenses.Find(l => l.SpdxID == spdx_id);

        if (license != null)
        {
            Debug.Assert(license.Filepath == _null_file); // promote only once!
            license.Filepath = filepath;
        }

        return license;
    }
}
