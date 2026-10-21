using Smartstore.Collections;
using Smartstore.Core.Content.Menus;
using Smartstore.Web.Rendering.Builders;

namespace MotorShop.WordPress;

public class AdminMenu : AdminMenuProvider
{
    protected override void BuildMenuCore(TreeNode<MenuItem> modulesNode)
    {
        var menuItem = new MenuItem().ToBuilder()
            .Text("WordPress")
            .ResKey("Plugins.FriendlyName.MotorShop.WordPress")
            .Icon("journal-richtext", "bi")
            .PermissionNames(WordPressPermissions.Read)
            .Action("Index", "MotorWordPressAdmin", new { area = "Admin" })
            .AsItem();

        modulesNode.Append(menuItem);
    }
}
