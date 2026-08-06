namespace NutManager.Core.Status;

public enum StatusSemanticState
{
    Unknown,
    Online,
    OnBattery,
    LowBattery,
    ReplaceBattery,
    Charging,
    Discharging,
    Bypass,
    OutputOff,
    Overloaded,
    Calibration
}
