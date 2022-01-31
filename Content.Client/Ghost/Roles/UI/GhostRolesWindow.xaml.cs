using System;
using Content.Shared.Ghost.Roles;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client.Ghost.Roles.UI
{
    [GenerateTypedNameReferences]
    public partial class GhostRolesWindow : DefaultWindow
    {
        public event Action<GhostRoleInfo>? RoleRequested;

        public void ClearEntries()
        {
            NoRolesMessage.Visible = true;
            EntryContainer.DisposeAllChildren();
        }

        public void AddEntry(GhostRoleInfo info)
        {
            NoRolesMessage.Visible = false;
            EntryContainer.AddChild(new GhostRolesEntry(info, _ => RoleRequested?.Invoke(info)));
        }
    }
}
