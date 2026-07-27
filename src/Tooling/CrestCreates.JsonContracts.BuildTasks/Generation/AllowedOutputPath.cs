namespace CrestCreates.JsonContracts.BuildTasks.Generation;

internal static class AllowedOutputPath
{
    public static bool Contains(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root);
        var normalizedCandidate = Path.GetFullPath(candidate);
        var relative = Path.GetRelativePath(normalizedRoot, normalizedCandidate);

        return !Path.IsPathRooted(relative)
            && !string.Equals(relative, "..", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }
}
