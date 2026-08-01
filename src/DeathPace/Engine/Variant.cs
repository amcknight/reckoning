namespace DeathPace.Engine;

/// <summary>Spinlab's variant concept: Hot = crossed the marker alive and in
/// flow (resources/momentum intact); Cold = respawned at the marker after a
/// death. Marker→exit times differ between the two, so bests are kept apart.</summary>
public enum Variant
{
    Hot,
    Cold,
}
