using System.Globalization;
using System.Text;
using UnityEngine;

// JSONL emitters for the telemetry log. Keys are compile-time literals
// (trusted), values are escaped, numbers are invariant-culture — under a
// comma-decimal locale every line would otherwise be unparseable.
public static class JsonLine
{
    public static string Build(float time, string wallClock, int episode, string type, params string[] fields)
    {
        var sb = new StringBuilder(128);
        sb.Append("{\"t\":").Append(Number(time, "0.###"));
        sb.Append(",\"wall\":\"").Append(Escape(wallClock)).Append('"');
        sb.Append(",\"episode\":").Append(episode.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"type\":\"").Append(Escape(type)).Append('"');
        if (fields != null)
        {
            foreach (string field in fields) sb.Append(',').Append(field);
        }
        return sb.Append('}').ToString();
    }

    public static string Field(string key, string value) => $"\"{key}\":\"{Escape(value)}\"";
    public static string Field(string key, float value) => $"\"{key}\":{Number(value, "0.###")}";
    public static string Field(string key, int value) => $"\"{key}\":{value.ToString(CultureInfo.InvariantCulture)}";
    public static string Field(string key, bool value) => $"\"{key}\":{(value ? "true" : "false")}";

    public static string Field(string key, Vector3 v) =>
        $"\"{key}\":[{Number(v.x, "0.##")},{Number(v.y, "0.##")},{Number(v.z, "0.##")}]";

    // Newlines matter as much as quotes here: one line per event is the whole
    // file format, and LogEvent is open to callers passing free text.
    public static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
    }

    static string Number(float value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
}
