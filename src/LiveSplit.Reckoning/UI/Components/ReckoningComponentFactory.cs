using System;
using LiveSplit.Model;
using LiveSplit.UI.Components;

[assembly: ComponentFactory(typeof(ReckoningComponentFactory))]

namespace LiveSplit.UI.Components;

public class ReckoningComponentFactory : IComponentFactory
{
    public string ComponentName => "Reckoning";
    public string Description => "Death-aware Best Possible Time for SMW kaizo: what finish is actually still possible from where death left you.";
    public ComponentCategory Category => ComponentCategory.Information;
    public IComponent Create(LiveSplitState state) => new ReckoningComponent(state);
    public string UpdateName => ComponentName;
    public string XMLURL => string.Empty;
    public string UpdateURL => string.Empty;
    public Version Version => Version.Parse("0.1.0");
}
