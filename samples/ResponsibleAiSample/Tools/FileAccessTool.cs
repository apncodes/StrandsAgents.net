using StrandsAgents.Core;

namespace ResponsibleAiSample.Tools;

public class FileAccessTool
{
    // RESPONSIBLE AI PRINCIPLE: Least Privilege
    // This tool only reads files from an explicit allow-list of permitted directories.
    // It refuses access to any path outside these directories, preventing the agent
    // from reading sensitive system files or traversing the filesystem arbitrarily.
    private static readonly string[] AllowedDirectories =
    [
        Path.Combine(Path.GetTempPath(), "safe_files"),
        Path.Combine(AppContext.BaseDirectory, "data")
    ];

    [Tool("Reads a file from an allowed directory. Only files in permitted paths can be accessed.")]
    public string ReadFile(string filePath)
    {
        // LEAST PRIVILEGE: Resolve the real path and verify it's within an allowed directory
        var realPath = Path.GetFullPath(filePath.Trim());

        if (!AllowedDirectories.Any(dir => realPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase)))
        {
            // AUDIT LOGGING: Security violations are logged (the AuditLogHookHandler
            // captures the tool call metadata; application logging captures the violation)
            Console.Error.WriteLine($"[SECURITY] Access denied to path: {filePath}");
            return "Error: Access denied. Path is not in an allowed directory.";
        }

        // ERROR HANDLING: Gracefully handle missing files and read errors
        if (!File.Exists(realPath))
            return $"Error: File '{filePath}' does not exist.";

        try
        {
            return File.ReadAllText(realPath);
        }
        catch (Exception ex)
        {
            return $"Error reading file: {ex.Message}";
        }
    }
}
