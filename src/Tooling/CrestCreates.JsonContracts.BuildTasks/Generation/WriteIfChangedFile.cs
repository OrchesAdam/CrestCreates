using System.Security.Cryptography;

namespace CrestCreates.JsonContracts.BuildTasks.Generation;

internal static class WriteIfChangedFile
{
    public static bool WriteIfChanged(string outputPath, byte[] newBytes, string temporaryDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path must not be empty.", nameof(outputPath));

        if (string.IsNullOrWhiteSpace(temporaryDirectory))
            throw new ArgumentException("Temporary directory must not be empty.", nameof(temporaryDirectory));

        if (File.Exists(outputPath))
        {
            var existingBytes = File.ReadAllBytes(outputPath);
            if (existingBytes.AsSpan().SequenceEqual(newBytes))
                return false;
        }

        Directory.CreateDirectory(temporaryDirectory);
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tempFile = Path.Combine(temporaryDirectory, Path.GetRandomFileName() + ".tmp");
        try
        {
            File.WriteAllBytes(tempFile, newBytes);
            File.Copy(tempFile, outputPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            catch
            {
            }
        }

        return true;
    }

    public static byte[] ComputeHash(byte[] content)
    {
        return SHA256.HashData(content);
    }
}
