namespace ScreenTune;

/// <summary>
/// Small plain-text log at %LOCALAPPDATA%\ScreenTune\log.txt, restarted on each launch.
/// Records what Windows reported (screens, HDR, refused ramps) for troubleshooting.
/// </summary>
internal static class Log
{
    private static readonly string FilePath = Path.Combine(App.DataDirectory, "log.txt");
    private static readonly object Gate = new();

    public static void Start()
    {
        try
        {
            Directory.CreateDirectory(App.DataDirectory);
            File.WriteAllText(FilePath, "");
        }
        catch (IOException)
        {
        }
    }

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
            }
        }
    }
}
