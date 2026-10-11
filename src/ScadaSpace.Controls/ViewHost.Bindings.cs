using ScadaSpace.Core;

namespace ScadaSpace.Controls;

public partial class ViewHost
{
    public IReadOnlyList<ViewHost> EmbeddedHosts => _embedded.AsReadOnly();
    public QualifiedValue Read(ComponentNode node, string property) => Instance?.Read(node, property) ?? QualifiedValue.Bad(Quality.BadDisabled, DateTimeOffset.UtcNow);
    private Scalar Value(ComponentNode node, string property, Scalar fallback = default)
    {
        var sample = Read(node, property);
        return sample.Quality == Quality.BadNotFound ? fallback : sample.Value;
    }
    private double Number(ComponentNode node, string property, double fallback = 0)
    { try { return Value(node, property, Scalar.From(fallback)).AsNumber(); } catch (InvalidOperationException) { return fallback; } }
    private string Text(ComponentNode node, string property, string fallback = "") => Value(node, property, Scalar.From(fallback)).ToString();
    private bool Flag(ComponentNode node, string property, bool fallback = false) => Value(node, property, Scalar.From(fallback)).AsBoolean();
    private void Write(ComponentNode node, string property, Scalar value)
    {
        if (_updating || IsEditing || Instance is null || Session is null) return;
        var old = Read(node, property);
        var result = Instance.Write(node, property, value, Session.Actor, old.Revision);
        if (!result.Success) Error?.Invoke(result.Message);
        ValueChanged?.Invoke(); Refresh();
    }
    private void Action(ComponentNode node, string eventName)
    {
        if (IsEditing || _updating || !node.Events.ContainsKey(eventName)) return;
        try { Session?.Dispatch(node, eventName); ValueChanged?.Invoke(); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException) { Error?.Invoke(ex.Message); }
    }
    private bool Writable(ComponentNode node, string property) => !IsEditing && Flag(node, "enabled", true) && Instance?.CanWrite(node, property) == true;
}
