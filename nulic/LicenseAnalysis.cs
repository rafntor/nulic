namespace nulic;

internal static class LicenseAnalysis
{
    internal static string? LookupSpdxIDByKeywords(string text)
    {
        // Handles short reference texts and multi-license bundle files where cosine similarity fails.
        // Collects ALL licenses present and returns a composite SPDX expression.
        bool has(string s) => text.Contains(s, StringComparison.OrdinalIgnoreCase);

        var found = new HashSet<string>(StringComparer.Ordinal);

        if (has("GNU Affero General Public License"))
            found.Add("AGPL-3.0-only");

        if (has("GNU Lesser General Public License"))
            found.Add(has("version 3") || has("LGPL-3") ? "LGPL-3.0-only" : "LGPL-2.1-only");

        if (has("GNU General Public License"))
            found.Add(has("version 3") || has("GPL-3") ? "GPL-3.0-only" : "GPL-2.0-only");

        if (has("Apache License") && has("2.0"))
            found.Add("Apache-2.0");

        if (has("Permission is hereby granted, free of charge") && has("above copyright notice and this permission notice"))
            found.Add("MIT");

        if (has("Redistribution and use in source and binary forms"))
            found.Add(has("Neither the name of") ? "BSD-3-Clause" : "BSD-2-Clause");

        if (has("Mozilla Public License") && has("2.0"))
            found.Add("MPL-2.0");

        if (has("Eclipse Public License"))
            found.Add(has("2.0") ? "EPL-2.0" : "EPL-1.0");

        if (has("ISC License") || (has("Permission to use, copy, modify") && has("ISC")))
            found.Add("ISC");

        if (has("OpenSSL") && has("free for commercial and non-commercial use"))
            found.Add("OpenSSL");

        if (has("Microsoft Public License") || has("MS-PL"))
            found.Add("MS-PL");

        if (has("This is free and unencumbered software released into the public domain"))
            found.Add("Unlicense");

        if (has("The Software shall be used for Good, not Evil"))
            found.Add("JSON");

        if (found.Count == 0) return null;

        // Apply WITH-exception to GPL even inside multi-license bundles
        foreach (var gplId in new[] { "GPL-2.0-only", "GPL-3.0-only" })
        {
            if (!found.Contains(gplId)) continue;
            var ex = DetectSpdxException(text);
            if (ex != null) { found.Remove(gplId); found.Add($"{gplId} WITH {ex}"); }
            break;
        }

        return string.Join(" AND ", found.OrderBy(x => x));
    }

    internal static string? DetectSpdxException(string text)
    {
        bool has(string s) => text.Contains(s, StringComparison.OrdinalIgnoreCase);

        if (has("Classpath") && has("special exception"))
            return "Classpath-exception-2.0";
        if (has("special exception") && has("instantiate") && has("must still be made available"))
            return "eCos-exception-2.0";
        if (has("eCos") && has("special exception"))
            return "eCos-exception-2.0";
        if (has("special exception") && (has("instantiate") || has("inline functions") || has("link it with")))
            return "LicenseRef-linking-exception";
        if (has("permission to link"))
            return "LicenseRef-linking-exception";

        return null;
    }

    internal static IEnumerable<string> LookupCopyrights(TextReader license_text)
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
