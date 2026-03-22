using System.Collections.Generic;

namespace D4Pressure.Models;

public record ConfigModel(List<KeyRowConfig> Rows);

public record KeyRowConfig(
    string KeyName,
    ushort ScanCode,
    string Mode,
    string ActionLabel,
    int DelayMs,
    bool IsEnabled,
    bool IsMouseInput,
    int MouseButtonIndex,
    string? IconBase64 = null);
