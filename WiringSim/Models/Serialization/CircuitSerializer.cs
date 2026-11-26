using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using WiringSim.Models.Enums;

namespace WiringSim.Models.Serialization;

public sealed class CircuitDocumentDto
{
    public string FormatVersion { get; set; } = "WiringSim-3.0";
    public string Title { get; set; } = "WiringSim";
    public DateTime SavedAt { get; set; } = DateTime.Now;
    public List<ComponentDto> Components { get; set; } = new();
    public List<WireDto> Wires { get; set; } = new();
}

public sealed class ComponentDto
{
    public string Id { get; set; } = string.Empty;
    public string TypeKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public int RotationAngle { get; set; }
    public bool IsFlippedHorizontal { get; set; }
    public bool IsFlippedVertical { get; set; }
    public Dictionary<string, string> Properties { get; set; } = new();
}

public sealed class WaypointDto
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class WireDto
{
    public string Id { get; set; } = string.Empty;
    public string FromComponentId { get; set; } = string.Empty;
    public string FromTerminalId { get; set; } = string.Empty;
    public string ToComponentId { get; set; } = string.Empty;
    public string ToTerminalId { get; set; } = string.Empty;
    public WireColorPreset ColorPreset { get; set; }
    public List<WaypointDto>? Waypoints { get; set; }
}

/// <summary>
/// Lưu / nạp sơ đồ mạch ra tệp .json và tạo ảnh chụp bộ nhớ tức thời phục vụ Undo / Redo.
/// </summary>
public static class CircuitSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    public static string SerializeToJsonString(
        IReadOnlyList<ElectricalComponent> components,
        IReadOnlyList<WireModel> wires,
        string title = "WiringSim",
        bool indented = false)
    {
        var doc = new CircuitDocumentDto
        {
            Title = title,
            SavedAt = DateTime.Now
        };

        foreach (var comp in components)
        {
            doc.Components.Add(new ComponentDto
            {
                Id = comp.Id,
                TypeKey = comp.TypeKey,
                DisplayName = comp.DisplayName,
                X = comp.X,
                Y = comp.Y,
                RotationAngle = comp.RotationAngle,
                IsFlippedHorizontal = comp.IsFlippedHorizontal,
                IsFlippedVertical = comp.IsFlippedVertical,
                Properties = comp.ExportProperties()
            });
        }

        foreach (var wire in wires)
        {
            doc.Wires.Add(new WireDto
            {
                Id = wire.Id,
                FromComponentId = wire.TerminalA.Owner.Id,
                FromTerminalId = wire.TerminalA.Id,
                ToComponentId = wire.TerminalB.Owner.Id,
                ToTerminalId = wire.TerminalB.Id,
                ColorPreset = wire.ColorPreset,
                Waypoints = wire.Waypoints.Count > 0
                    ? wire.Waypoints.Select(p => new WaypointDto { X = Math.Round(p.X, 1), Y = Math.Round(p.Y, 1) }).ToList()
                    : null
            });
        }

        return JsonSerializer.Serialize(doc, indented ? JsonOptions : CompactOptions);
    }

    public static (List<ElectricalComponent> Components, List<WireModel> Wires, string Title) DeserializeFromJsonString(string json)
    {
        var doc = JsonSerializer.Deserialize<CircuitDocumentDto>(json, JsonOptions)
                  ?? throw new InvalidDataException("Invalid JSON document.");

        var components = new List<ElectricalComponent>();
        var byId = new Dictionary<string, ElectricalComponent>(StringComparer.OrdinalIgnoreCase);

        foreach (var dto in doc.Components)
        {
            var instance = ComponentRegistry.Create(dto.TypeKey);
            typeof(ElectricalComponent)
                .GetProperty(nameof(ElectricalComponent.Id))!
                .SetValue(instance, string.IsNullOrWhiteSpace(dto.Id) ? instance.Id : dto.Id);

            if (!string.IsNullOrWhiteSpace(dto.DisplayName))
                instance.DisplayName = dto.DisplayName;

            instance.X = dto.X;
            instance.Y = dto.Y;
            instance.RotationAngle = dto.RotationAngle;
            instance.IsFlippedHorizontal = dto.IsFlippedHorizontal;
            instance.IsFlippedVertical = dto.IsFlippedVertical;

            if (dto.Properties is not null)
                instance.ImportProperties(dto.Properties);

            components.Add(instance);
            byId[instance.Id] = instance;
        }

        var wires = new List<WireModel>();
        foreach (var wDto in doc.Wires)
        {
            if (!byId.TryGetValue(wDto.FromComponentId, out var compA) ||
                !byId.TryGetValue(wDto.ToComponentId, out var compB))
                continue;

            var termA = ResolveTerminalCompatible(compA, wDto.FromTerminalId);
            var termB = ResolveTerminalCompatible(compB, wDto.ToTerminalId);
            if (termA is null || termB is null || termA == termB)
                continue;

            var waypoints = wDto.Waypoints?.Select(wp => new Point(wp.X, wp.Y));
            wires.Add(new WireModel(termA, termB, wDto.ColorPreset, waypoints));
        }

        return (components, wires, doc.Title);
    }

    public static void SaveToFile(
        string filePath,
        IReadOnlyList<ElectricalComponent> components,
        IReadOnlyList<WireModel> wires,
        string title = "WiringSim")
    {
        string json = SerializeToJsonString(components, wires, title, indented: true);
        File.WriteAllText(filePath, json);
    }

    public static (List<ElectricalComponent> Components, List<WireModel> Wires, string Title) LoadFromFile(string filePath)
    {
        string json = File.ReadAllText(filePath);
        return DeserializeFromJsonString(json);
    }

    private static Terminal? ResolveTerminalCompatible(ElectricalComponent comp, string terminalId)
    {
        var direct = comp.GetTerminal(terminalId);
        if (direct is not null) return direct;

        return terminalId.ToUpperInvariant() switch
        {
            "NC1" or "NO1" => comp.GetTerminal("COM"),
            "NC2" => comp.GetTerminal("NC"),
            "NO2" => comp.GetTerminal("NO"),
            _ => null
        };
    }
}

/// <summary>
/// Quản lý lịch sử Hoàn tác (Undo) &amp; Làm lại (Redo) chuẩn phần mềm CAD.
/// </summary>
public sealed class CircuitHistoryManager
{
    private const int MaxHistoryDepth = 50;
    private readonly List<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();
    private string _currentSnapshot = string.Empty;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public event Action? StateChanged;

    public void Reset(IReadOnlyList<ElectricalComponent> components, IReadOnlyList<WireModel> wires)
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _currentSnapshot = CircuitSerializer.SerializeToJsonString(components, wires);
        StateChanged?.Invoke();
    }

    public void RecordSnapshot(IReadOnlyList<ElectricalComponent> components, IReadOnlyList<WireModel> wires)
    {
        string next = CircuitSerializer.SerializeToJsonString(components, wires);
        if (next == _currentSnapshot) return;

        if (!string.IsNullOrEmpty(_currentSnapshot))
        {
            _undoStack.Add(_currentSnapshot);
            if (_undoStack.Count > MaxHistoryDepth)
                _undoStack.RemoveAt(0);
        }

        _currentSnapshot = next;
        _redoStack.Clear();
        StateChanged?.Invoke();
    }

    public (List<ElectricalComponent> Components, List<WireModel> Wires)? Undo()
    {
        if (_undoStack.Count == 0) return null;

        _redoStack.Push(_currentSnapshot);
        string prev = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        _currentSnapshot = prev;
        StateChanged?.Invoke();

        var (comps, wires, _) = CircuitSerializer.DeserializeFromJsonString(prev);
        return (comps, wires);
    }

    public (List<ElectricalComponent> Components, List<WireModel> Wires)? Redo()
    {
        if (_redoStack.Count == 0) return null;

        _undoStack.Add(_currentSnapshot);
        string next = _redoStack.Pop();
        _currentSnapshot = next;
        StateChanged?.Invoke();

        var (comps, wires, _) = CircuitSerializer.DeserializeFromJsonString(next);
        return (comps, wires);
    }
}
