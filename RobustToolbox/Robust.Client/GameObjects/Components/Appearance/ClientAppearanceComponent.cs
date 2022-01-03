using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;

namespace Robust.Client.GameObjects;

/// <summary>
/// This is the client instance of <see cref="AppearanceComponent"/>.
/// </summary>
[RegisterComponent]
[ComponentReference(typeof(AppearanceComponent))]
public sealed class ClientAppearanceComponent : AppearanceComponent
{
    [ViewVariables]
    private bool _appearanceDirty;

    [ViewVariables]
    [DataField("visuals")]
    internal List<AppearanceVisualizer> Visualizers = new();

    protected override void MarkDirty()
    {
        if (_appearanceDirty)
            return;

        EntitySystem.Get<AppearanceSystem>().EnqueueUpdate(this);
        _appearanceDirty = true;
    }

    protected override void Initialize()
    {
        base.Initialize();

        foreach (var visual in Visualizers)
        {
            visual.InitializeEntity(Owner);
        }

        MarkDirty();
    }

    internal void UnmarkDirty()
    {
        _appearanceDirty = false;
    }
}
