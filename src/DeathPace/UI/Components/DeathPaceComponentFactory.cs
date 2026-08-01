using System;
using LiveSplit.Model;
using LiveSplit.UI.Components;

[assembly: ComponentFactory(typeof(DeathPaceComponentFactory))]

namespace LiveSplit.UI.Components;

public class DeathPaceComponentFactory : IComponentFactory
{
    public string ComponentName => "SMW Death Pace";
    public string Description => "Death-aware Run Prediction for SMW kaizo: any comparison, with learned post-death recovery paces and a damage-style time-lost hit.";
    public ComponentCategory Category => ComponentCategory.Information;
    public IComponent Create(LiveSplitState state) => new DeathPaceComponent(state);
    public string UpdateName => ComponentName;
    public string XMLURL => string.Empty;
    public string UpdateURL => string.Empty;
    public Version Version => Version.Parse("0.1.0");
}
