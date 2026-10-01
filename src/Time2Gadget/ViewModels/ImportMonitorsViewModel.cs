using Time2Gadget.Services;

namespace Time2Gadget.ViewModels;

/// <summary>Куда перенести настройки монитора из архива: текущий монитор или «не переносить» (Id = null).</summary>
public sealed record MonitorChoice(string? Id, string Label);

/// <summary>Строка окна сопоставления: монитор из архива → выбранный текущий монитор.</summary>
public sealed class MonitorMapRow
{
    public MonitorMapRow(int number, string label, string contents, IReadOnlyList<MonitorChoice> choices, MonitorChoice selected)
    {
        Number = number;
        Label = label;
        Contents = contents;
        Choices = choices;
        Selected = selected;
    }

    public int Number { get; }
    public string Label { get; }
    public string Contents { get; }
    public IReadOnlyList<MonitorChoice> Choices { get; }
    public MonitorChoice Selected { get; set; }
}

/// <summary>
/// Окно «Импорт — мониторы» (докладка 2026-10-01): для каждого монитора из архива — что в нём есть и на какой из текущих
/// мониторов перенести его настройки. По умолчанию — монитор с тем же разрешением, иначе с тем же номером, иначе «не переносить».
/// </summary>
public sealed class ImportMonitorsViewModel
{
    public ImportMonitorsViewModel(WallpaperPackageData data)
    {
        var current = WallpaperService.Monitors();
        var choices = new List<MonitorChoice> { new(null, "не переносить") };
        choices.AddRange(current.Select(m => new MonitorChoice(m.Id, $"Монитор {m.Number} — {m.Bounds.Width}×{m.Bounds.Height}")));
        var taken = new HashSet<string>();

        foreach (var n in data.MonitorNumbers())
        {
            var size = data.MonitorSizes.TryGetValue(n, out var s) ? s : null;
            var match = current.FirstOrDefault(m => size is not null && $"{m.Bounds.Width}×{m.Bounds.Height}" == size && !taken.Contains(m.Id))
                        ?? current.FirstOrDefault(m => m.Number == n && !taken.Contains(m.Id));
            if (match is not null) taken.Add(match.Id);
            var selected = match is null ? choices[0] : choices.First(c => c.Id == match.Id);
            Rows.Add(new MonitorMapRow(n, size is null ? $"Монитор {n}" : $"Монитор {n} — {size}", Describe(data, n), choices, selected));
        }
    }

    public List<MonitorMapRow> Rows { get; } = new();

    /// <summary>Номер монитора в архиве → текущий монитор (только выбранные).</summary>
    public Dictionary<int, string> Map() => Rows.Where(r => r.Selected.Id is not null).ToDictionary(r => r.Number, r => r.Selected.Id!);

    private static string Describe(WallpaperPackageData data, int n)
    {
        var parts = new List<string>();
        if (data.StaticImages.ContainsKey(n)) parts.Add("статичная заставка: картинка");
        else if (data.StaticModes.ContainsKey(n) || data.StaticColors.ContainsKey(n)) parts.Add("статичная заставка: режим/цвет");
        if (data.SlideshowMonitors.TryGetValue(n, out var slots))
            parts.Add($"слайдшоу: заполнено шагов {slots.Count(s => s is { IsEmpty: false })}");
        return parts.Count == 0 ? "без своих настроек" : string.Join("; ", parts);
    }
}
