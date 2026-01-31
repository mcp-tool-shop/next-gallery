using System.Security.Cryptography;
using System.Text;

namespace Gallery.Domain;

/// <summary>
/// Handles workspace path normalization and key derivation.
/// Must produce identical keys as CodeComfy Python implementation.
/// Contract: test-vectors/workspace_normalization.v0.2.json
/// </summary>
public static class WorkspaceKey
{
    /// <summary>
    /// Normalize a workspace path to canonical form.
    /// Algorithm matches workspace_normalization.v0.2 contract.
    /// </summary>
    /// <param name="workspacePath">Raw workspace path from user/CLI</param>
    /// <returns>Canonical lowercase path with forward slashes</returns>
    /// <exception cref="ArgumentException">If path is empty, whitespace-only, or contains null bytes</exception>
    public static string NormalizePath(string workspacePath)
    {
        // Fail-fast validation
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            throw new ArgumentException("Workspace path cannot be empty or whitespace-only", nameof(workspacePath));
        }

        if (workspacePath.Contains('\0'))
        {
            throw new ArgumentException("Workspace path cannot contain null bytes", nameof(workspacePath));
        }

        // Step 1: OS resolution (authoritative boundary)
        // Path.GetFullPath handles: relative paths, ., .., case normalization on Windows
        var resolved = Path.GetFullPath(workspacePath);

        // Step 3: Convert backslashes to forward slashes
        var normalized = resolved.Replace('\\', '/');

        // Step 3b: Defensive clamp for /// edge case
        // (Should never happen after GetFullPath, but guard anyway)
        if (normalized.StartsWith("///"))
        {
            normalized = "//" + normalized.TrimStart('/');
        }

        // Step 4: Unicode NFC normalization
        normalized = normalized.Normalize(NormalizationForm.FormC);

        // Step 5: Invariant lowercase
        normalized = normalized.ToLowerInvariant();

        // Step 6-7: Trailing slash handling
        if (IsUncRoot(normalized))
        {
            // UNC share roots: //server/share -> no trailing slash
            normalized = normalized.TrimEnd('/');
        }
        else if (normalized.Length == 2 && normalized[1] == ':')
        {
            // Bare drive letter: c: -> c:/
            normalized = normalized + "/";
        }
        else if (normalized.Length > 3 && normalized.EndsWith('/'))
        {
            // Regular paths: strip trailing slash
            normalized = normalized.TrimEnd('/');
        }

        return normalized;
    }

    /// <summary>
    /// Compute the workspace key from a path.
    /// Formula: sha256(UTF8(NFC(canon_path))).hex()[0:32]
    /// </summary>
    /// <param name="workspacePath">Raw workspace path from user/CLI</param>
    /// <returns>32-character lowercase hex string</returns>
    public static string ComputeKey(string workspacePath)
    {
        var canonPath = NormalizePath(workspacePath);
        var bytes = Encoding.UTF8.GetBytes(canonPath);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    /// <summary>
    /// Check if a normalized path is a UNC share root (//server/share).
    /// </summary>
    private static bool IsUncRoot(string normalizedPath)
    {
        if (!normalizedPath.StartsWith("//"))
        {
            return false;
        }

        // Split and filter empty segments
        var parts = normalizedPath[2..]
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        // UNC root has exactly 2 parts: server and share
        return parts.Length == 2;
    }
}
