using System.IO;
namespace LummaSimulator.Configuration;

/// <summary>Relative paths are based on the executable directory, never the working directory.</summary>
public static class RuntimePaths
{
    public static string Resolve(string path, string? relativeBase = null) =>
        Path.GetFullPath(Environment.ExpandEnvironmentVariables(path), relativeBase ?? AppContext.BaseDirectory);
    public static string DataRoot(string? configured = null)
    {
        var value = Environment.GetEnvironmentVariable("LUMMA_DATA_DIRECTORY");
        if (string.IsNullOrWhiteSpace(value)) value = configured;
        return !string.IsNullOrWhiteSpace(value) ? Resolve(value) : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LummaSimulator");
    }
    public static string Logs(string? configured = null) => Resolve(
        Environment.GetEnvironmentVariable("LUMMA_LOG_DIRECTORY") ?? configured ?? "Logs", DataRoot());
    public static string Exports(string? configured = null) => Resolve(
        Environment.GetEnvironmentVariable("LUMMA_EXPORT_DIRECTORY") ?? configured ?? Logs(), DataRoot());
}
