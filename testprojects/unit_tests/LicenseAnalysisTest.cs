namespace unit_tests;

[TestClass]
public class LicenseAnalysisTest
{
    // ── LookupSpdxIDByKeywords ─────────────────────────────────────────────

    [TestMethod] public void MIT_detected()
        => Assert.AreEqual("MIT",
            nulic.LicenseAnalysis.LookupSpdxIDByKeywords(
                "Permission is hereby granted, free of charge, to any person... " +
                "above copyright notice and this permission notice shall be included"));

    [TestMethod] public void Apache20_detected()
        => Assert.AreEqual("Apache-2.0",
            nulic.LicenseAnalysis.LookupSpdxIDByKeywords(
                "Apache License\nVersion 2.0, January 2004"));

    [TestMethod] public void BSD3_detected()
        => Assert.AreEqual("BSD-3-Clause",
            nulic.LicenseAnalysis.LookupSpdxIDByKeywords(
                "Redistribution and use in source and binary forms, with or without modification... " +
                "Neither the name of the copyright holder"));

    [TestMethod] public void BSD2_detected()
        => Assert.AreEqual("BSD-2-Clause",
            nulic.LicenseAnalysis.LookupSpdxIDByKeywords(
                "Redistribution and use in source and binary forms, with or without modification"));

    [TestMethod] public void MPL20_detected()
        => Assert.AreEqual("MPL-2.0",
            nulic.LicenseAnalysis.LookupSpdxIDByKeywords(
                "Mozilla Public License 2.0"));

    [TestMethod] public void EPL2_detected()
        => Assert.AreEqual("EPL-2.0",
            nulic.LicenseAnalysis.LookupSpdxIDByKeywords(
                "Eclipse Public License 2.0"));

    [TestMethod] public void EPL1_detected()
        => Assert.AreEqual("EPL-1.0",
            nulic.LicenseAnalysis.LookupSpdxIDByKeywords(
                "Eclipse Public License Version 1.0"));

    [TestMethod] public void ISC_detected_explicit()
        => Assert.AreEqual("ISC",
            nulic.LicenseAnalysis.LookupSpdxIDByKeywords("ISC License"));

    [TestMethod] public void ISC_detected_keywords()
        => Assert.AreEqual("ISC",
            nulic.LicenseAnalysis.LookupSpdxIDByKeywords(
                "Permission to use, copy, modify ISC software without restriction"));

    [TestMethod] public void OpenSSL_detected()
        => Assert.AreEqual("OpenSSL",
            nulic.LicenseAnalysis.LookupSpdxIDByKeywords(
                "OpenSSL library is free for commercial and non-commercial use as long as the following conditions are adhered to."));

    [TestMethod] public void MSPL_detected_name()
        => Assert.AreEqual("MS-PL",
            nulic.LicenseAnalysis.LookupSpdxIDByKeywords("Microsoft Public License (MS-PL)"));

    [TestMethod] public void MSPL_detected_acronym()
        => Assert.AreEqual("MS-PL",
            nulic.LicenseAnalysis.LookupSpdxIDByKeywords("MS-PL terms apply"));

    [TestMethod] public void Unlicense_detected()
        => Assert.AreEqual("Unlicense",
            nulic.LicenseAnalysis.LookupSpdxIDByKeywords(
                "This is free and unencumbered software released into the public domain."));

    [TestMethod] public void JSON_license_detected()
        => Assert.AreEqual("JSON",
            nulic.LicenseAnalysis.LookupSpdxIDByKeywords(
                "The Software shall be used for Good, not Evil."));

    [TestMethod] public void GPL2_detected()
        => Assert.AreEqual("GPL-2.0-only",
            nulic.LicenseAnalysis.LookupSpdxIDByKeywords(
                "GNU General Public License version 2"));

    [TestMethod] public void GPL3_detected()
        => Assert.AreEqual("GPL-3.0-only",
            nulic.LicenseAnalysis.LookupSpdxIDByKeywords(
                "GNU General Public License version 3"));

    [TestMethod] public void LGPL2_detected()
        => Assert.AreEqual("LGPL-2.1-only",
            nulic.LicenseAnalysis.LookupSpdxIDByKeywords(
                "GNU Lesser General Public License version 2"));

    [TestMethod] public void LGPL3_detected()
        => Assert.AreEqual("LGPL-3.0-only",
            nulic.LicenseAnalysis.LookupSpdxIDByKeywords(
                "GNU Lesser General Public License version 3"));

    [TestMethod] public void AGPL_detected()
        => Assert.AreEqual("AGPL-3.0-only",
            nulic.LicenseAnalysis.LookupSpdxIDByKeywords(
                "GNU Affero General Public License"));

    [TestMethod] public void Unknown_returns_null()
        => Assert.IsNull(nulic.LicenseAnalysis.LookupSpdxIDByKeywords(
            "All rights reserved. Proprietary and confidential."));

    [TestMethod] public void Multi_license_bundle_returns_composite()
    {
        var text = "Apache License\nVersion 2.0, January 2004\n" +
                   "Permission is hereby granted, free of charge, to any person... " +
                   "above copyright notice and this permission notice";
        var result = nulic.LicenseAnalysis.LookupSpdxIDByKeywords(text);
        // Sorted: Apache-2.0 AND MIT
        Assert.AreEqual("Apache-2.0 AND MIT", result);
    }

    [TestMethod] public void GPL2_with_classpath_exception()
    {
        var text = "GNU General Public License version 2\n" +
                   "Classpath special exception to the GPL";
        Assert.AreEqual("GPL-2.0-only WITH Classpath-exception-2.0",
            nulic.LicenseAnalysis.LookupSpdxIDByKeywords(text));
    }

    [TestMethod] public void GPL2_with_ecos_exception()
    {
        var text = "GNU General Public License version 2\n" +
                   "special exception that you may instantiate templates or use macros... " +
                   "must still be made available under the terms of the GPL";
        Assert.AreEqual("GPL-2.0-only WITH eCos-exception-2.0",
            nulic.LicenseAnalysis.LookupSpdxIDByKeywords(text));
    }

    // ── DetectSpdxException ────────────────────────────────────────────────

    [TestMethod] public void Exception_classpath()
        => Assert.AreEqual("Classpath-exception-2.0",
            nulic.LicenseAnalysis.DetectSpdxException(
                "Classpath special exception to the GPL"));

    [TestMethod] public void Exception_ecos_via_phrases()
        => Assert.AreEqual("eCos-exception-2.0",
            nulic.LicenseAnalysis.DetectSpdxException(
                "special exception that you may instantiate... must still be made available"));

    [TestMethod] public void Exception_ecos_via_name()
        => Assert.AreEqual("eCos-exception-2.0",
            nulic.LicenseAnalysis.DetectSpdxException(
                "eCos special exception"));

    [TestMethod] public void Exception_linking_via_instantiate()
        => Assert.AreEqual("LicenseRef-linking-exception",
            nulic.LicenseAnalysis.DetectSpdxException(
                "special exception to allow you to instantiate or use inline functions"));

    [TestMethod] public void Exception_linking_via_permission_to_link()
        => Assert.AreEqual("LicenseRef-linking-exception",
            nulic.LicenseAnalysis.DetectSpdxException(
                "permission to link this library with independent modules"));

    [TestMethod] public void Exception_none_returns_null()
        => Assert.IsNull(nulic.LicenseAnalysis.DetectSpdxException(
            "Apache License Version 2.0"));

    // ── LookupCopyrights ───────────────────────────────────────────────────

    [TestMethod]
    public void Copyright_c_detected()
    {
        using var reader = new StringReader("Copyright (c) 2024 Alice");
        var result = nulic.LicenseAnalysis.LookupCopyrights(reader).ToArray();
        Assert.AreEqual(1, result.Length);
        StringAssert.Contains(result[0], "2024 Alice");
    }

    [TestMethod]
    public void Copyright_symbol_detected()
    {
        using var reader = new StringReader("Copyright © 2024 Bob Corp");
        var result = nulic.LicenseAnalysis.LookupCopyrights(reader).ToArray();
        Assert.AreEqual(1, result.Length);
        StringAssert.Contains(result[0], "2024 Bob Corp");
    }

    [TestMethod]
    public void Copyright_case_insensitive()
    {
        using var reader = new StringReader("COPYRIGHT (C) 2024 Alice");
        var result = nulic.LicenseAnalysis.LookupCopyrights(reader).ToArray();
        Assert.AreEqual(1, result.Length);
    }

    [TestMethod]
    public void Copyright_multiple_lines_all_returned()
    {
        using var reader = new StringReader(
            "Copyright (c) 2020 Alice\nsome text\nCopyright (c) 2021 Bob");
        var result = nulic.LicenseAnalysis.LookupCopyrights(reader).ToArray();
        Assert.AreEqual(2, result.Length);
    }

    [TestMethod]
    public void Copyright_leading_whitespace_stripped_at_keyword()
    {
        // Substring starts at "copyright", so leading spaces before it are dropped
        using var reader = new StringReader("   Copyright (c) 2024 Alice");
        var result = nulic.LicenseAnalysis.LookupCopyrights(reader).ToArray();
        Assert.AreEqual(1, result.Length);
        Assert.IsTrue(result[0].StartsWith("Copyright", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Copyright_no_match_returns_empty()
    {
        using var reader = new StringReader("MIT License\nPermission is hereby granted");
        var result = nulic.LicenseAnalysis.LookupCopyrights(reader).ToArray();
        Assert.AreEqual(0, result.Length);
    }

    [TestMethod]
    public void Copyright_empty_input_returns_empty()
    {
        using var reader = new StringReader("");
        var result = nulic.LicenseAnalysis.LookupCopyrights(reader).ToArray();
        Assert.AreEqual(0, result.Length);
    }
}
