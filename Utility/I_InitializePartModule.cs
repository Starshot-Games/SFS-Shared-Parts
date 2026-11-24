public interface I_InitializePartModule
{
    int Priority { get; }
    void Initialize();
}

// Called on parts created fresh from the pick grid, not on parts loaded from a save
public interface I_NewPartModule
{
    void OnNewPart();
}