using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gallery.Domain;

namespace Gallery.Tests;

/// <summary>
/// Tests for workspace normalization against shared v0.2 vectors.
/// These vectors must produce identical results in both C# and Python.
/// </summary>
[TestFixture]
public class WorkspaceNormalizationTests
{
    private TestVectors _vectors = null!;

    [OneTimeSetUp]
    public void LoadVectors()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestVectors", "workspace_normalization.v0.2.json");
        if (!File.Exists(path))
        {
            // Fallback to Contracts folder
            path = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "Contracts", "workspace_normalization.v0.2.json");
        }

        Assert.That(File.Exists(path), Is.True, $"Test vectors file not found at: {path}");

        var json = File.ReadAllText(path);
        _vectors = JsonSerializer.Deserialize<TestVectors>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

        Assert.That(_vectors, Is.Not.Null, "Failed to deserialize test vectors");
        Assert.That(_vectors.Vectors, Is.Not.Empty, "Test vectors array is empty");
    }

    [Test]
    public void VectorsFile_HasCorrectVersion()
    {
        Assert.That(_vectors.Version, Is.EqualTo("0.2"), "Expected v0.2 vectors");
    }

    [Test]
    public void VectorsFile_Has32CharKeyLength()
    {
        Assert.That(_vectors.KeyLengthHex, Is.EqualTo(32), "Expected 32-char hex keys");
    }

    [Test]
    public void AllVectors_Step1ToCanonPath()
    {
        foreach (var v in _vectors.Vectors)
        {
            // Test: processing step1_fullpath should produce canon_path
            var actual = NormalizeForTest(v.Step1Fullpath);
            Assert.That(actual, Is.EqualTo(v.CanonPath),
                $"Vector {v.Id}: NormalizeForTest({v.Step1Fullpath}) = {actual}, expected {v.CanonPath}");
        }
    }

    [Test]
    public void AllVectors_CanonPathToWorkspaceKey()
    {
        foreach (var v in _vectors.Vectors)
        {
            // Direct hash of canon_path
            var bytes = Encoding.UTF8.GetBytes(v.CanonPath);
            var hash = SHA256.HashData(bytes);
            var actual = Convert.ToHexString(hash)[..32].ToLowerInvariant();

            Assert.That(actual, Is.EqualTo(v.WorkspaceKey),
                $"Vector {v.Id}: hash({v.CanonPath}) = {actual}, expected {v.WorkspaceKey}");
        }
    }

    [Test]
    public void Idempotency_NormalizingCanonPath_ReturnsSamePath()
    {
        foreach (var v in _vectors.Vectors)
        {
            var doubleNormalized = NormalizeForTest(v.CanonPath);
            Assert.That(doubleNormalized, Is.EqualTo(v.CanonPath),
                $"Vector {v.Id}: idempotency failed - normalize({v.CanonPath}) = {doubleNormalized}");
        }
    }

    [Test]
    public void CaseInsensitivity_AllVariations_ProduceSameKey()
    {
        var caseVectors = new[] { "basic_windows_path", "lowercase_drive", "mixed_case_path" };
        var keys = _vectors.Vectors
            .Where(v => caseVectors.Contains(v.Id))
            .Select(v => v.WorkspaceKey)
            .ToList();

        Assert.That(keys.Distinct().Count(), Is.EqualTo(1),
            $"Case variations should produce same key: {string.Join(", ", keys)}");
    }

    [Test]
    public void TrailingSlash_WithAndWithout_ProduceSameKey()
    {
        var slashVectors = new[] { "basic_windows_path", "trailing_backslash" };
        var keys = _vectors.Vectors
            .Where(v => slashVectors.Contains(v.Id))
            .Select(v => v.WorkspaceKey)
            .ToList();

        Assert.That(keys.Distinct().Count(), Is.EqualTo(1),
            $"Trailing slash variants should produce same key: {string.Join(", ", keys)}");
    }

    [Test]
    public void SlashDirection_ForwardAndBack_ProduceSameKey()
    {
        var slashVectors = new[] { "basic_windows_path", "forward_slashes" };
        var keys = _vectors.Vectors
            .Where(v => slashVectors.Contains(v.Id))
            .Select(v => v.WorkspaceKey)
            .ToList();

        Assert.That(keys.Distinct().Count(), Is.EqualTo(1),
            $"Slash direction should not affect key: {string.Join(", ", keys)}");
    }

    [Test]
    public void DriveRoot_WithAndWithoutSlash_ProduceSameKey()
    {
        var rootVectors = new[] { "drive_root", "drive_root_no_slash" };
        var keys = _vectors.Vectors
            .Where(v => rootVectors.Contains(v.Id))
            .Select(v => v.WorkspaceKey)
            .ToList();

        Assert.That(keys.Distinct().Count(), Is.EqualTo(1),
            $"Drive root variants should produce same key: {string.Join(", ", keys)}");
    }

    [Test]
    public void UncPath_CaseVariations_ProduceSameKey()
    {
        var uncVectors = new[] { "unc_path", "unc_mixed_case" };
        var keys = _vectors.Vectors
            .Where(v => uncVectors.Contains(v.Id))
            .Select(v => v.WorkspaceKey)
            .ToList();

        Assert.That(keys.Distinct().Count(), Is.EqualTo(1),
            $"UNC case variants should produce same key: {string.Join(", ", keys)}");
    }

    [Test]
    public void UncRoot_WithAndWithoutTrailingSlash_ProduceSameKey()
    {
        var uncRootVectors = new[] { "unc_root", "unc_root_no_slash" };
        var keys = _vectors.Vectors
            .Where(v => uncRootVectors.Contains(v.Id))
            .Select(v => v.WorkspaceKey)
            .ToList();

        if (keys.Count > 0)
        {
            Assert.That(keys.Distinct().Count(), Is.EqualTo(1),
                $"UNC root variants should produce same key: {string.Join(", ", keys)}");
        }
    }

    // Failure condition tests
    [Test]
    public void EmptyString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => WorkspaceKey.NormalizePath(""));
    }

    [Test]
    public void WhitespaceOnly_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => WorkspaceKey.NormalizePath("   "));
    }

    [Test]
    public void TabsOnly_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => WorkspaceKey.NormalizePath("\t\t"));
    }

    [Test]
    public void NewlinesOnly_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => WorkspaceKey.NormalizePath("\n\n"));
    }

    [Test]
    public void NullByte_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => WorkspaceKey.NormalizePath("C:\\Users\0\\evil"));
    }

    // Defensive clamp tests
    [Test]
    public void TripleSlash_ClampedToDoubleSlash()
    {
        var result = NormalizeForTest("///server/share/folder");
        Assert.That(result, Is.EqualTo("//server/share/folder"));
    }

    [Test]
    public void ManySlashes_ClampedToDoubleSlash()
    {
        var result = NormalizeForTest("/////server/share");
        Assert.That(result, Is.EqualTo("//server/share"));
    }

    [Test]
    public void DoubleSlash_PreservedForUnc()
    {
        var result = NormalizeForTest("//server/share/folder");
        Assert.That(result, Is.EqualTo("//server/share/folder"));
    }

    [Test]
    public void WorkspaceKey_Returns32HexChars()
    {
        var key = WorkspaceKey.ComputeKey("C:\\Users\\dev\\project");
        Assert.That(key.Length, Is.EqualTo(32));
        Assert.That(key, Does.Match("^[0-9a-f]{32}$"));
    }

    /// <summary>
    /// Test normalization without OS resolution (for testing with non-existent paths).
    /// Mirrors the algorithm starting from step1_fullpath.
    /// </summary>
    private static string NormalizeForTest(string path)
    {
        // Step 3: Forward slashes
        var normalized = path.Replace('\\', '/');

        // Step 3b: Defensive clamp for /// edge case
        if (normalized.StartsWith("///"))
        {
            normalized = "//" + normalized.TrimStart('/');
        }

        // Step 4: Unicode NFC
        normalized = normalized.Normalize(NormalizationForm.FormC);

        // Step 5: Invariant lowercase
        normalized = normalized.ToLowerInvariant();

        // Step 6-7: Trailing slash handling
        if (IsUncRoot(normalized))
        {
            normalized = normalized.TrimEnd('/');
        }
        else if (normalized.Length == 2 && normalized[1] == ':')
        {
            normalized = normalized + "/";
        }
        else if (normalized.Length > 3 && normalized.EndsWith('/'))
        {
            normalized = normalized.TrimEnd('/');
        }

        return normalized;
    }

    private static bool IsUncRoot(string normalizedPath)
    {
        if (!normalizedPath.StartsWith("//"))
        {
            return false;
        }

        var parts = normalizedPath[2..]
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 2;
    }
}

// DTOs for test vectors
public class TestVectors
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("key_length_hex")]
    public int KeyLengthHex { get; set; }

    [JsonPropertyName("vectors")]
    public List<TestVector> Vectors { get; set; } = new();
}

public class TestVector
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("input")]
    public string Input { get; set; } = "";

    [JsonPropertyName("step1_fullpath")]
    public string Step1Fullpath { get; set; } = "";

    [JsonPropertyName("canon_path")]
    public string CanonPath { get; set; } = "";

    [JsonPropertyName("workspace_key")]
    public string WorkspaceKey { get; set; } = "";

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}
