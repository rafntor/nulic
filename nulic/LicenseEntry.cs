namespace nulic;

internal record LicenseEntry(
    string Id,
    string Version,
    string[] Authors,
    string? ProjectUrl,
    string? Copyright,
    string License,
    string? LicenseUrl,
    string[] LicenseFiles
);
