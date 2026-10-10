using System.Text.Json;

return Suite.Execute();

internal static class Suite
{
    private static readonly List<(string Name, Action Body)> Tests = [];
    public static void Add(string name, Action body) => Tests.Add((name, body));
    public static void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
    public static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}"); }
    public static void Throws<T>(Action body) where T : Exception { try { body(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    public static int Execute()
    {
        var results = new List<object>(); var failures = 0;
        foreach (var (name, body) in Tests)
        {
            try { body(); Console.WriteLine("PASS " + name); results.Add(new { name, passed = true }); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + "\n" + ex); results.Add(new { name, passed = false, error = ex.ToString() }); }
        }
        Directory.CreateDirectory("TestResults");
        File.WriteAllText("TestResults/parity-results.json", JsonSerializer.Serialize(new { tests = Tests.Count, failures, results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"RESULT {Tests.Count - failures}/{Tests.Count} passed");
        return failures == 0 ? 0 : 1;
    }
}
