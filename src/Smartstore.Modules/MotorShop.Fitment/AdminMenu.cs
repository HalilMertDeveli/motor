using Smartstore.Collections;
using Smartstore.Core.Content.Menus;
using Smartstore.Web.Rendering.Builders;

namespace MotorShop.Fitment;

public class AdminMenu : AdminMenuProvider
{
    protected override void BuildMenuCore(TreeNode<MenuItem> modulesNode)
    {
        var menuItem = new MenuItem().ToBuilder()
            .Text("Motor Fitment")
            .ResKey("Plugins.FriendlyName.MotorShop.Fitment")
            .Icon("bicycle", "bi")
            .PermissionNames(Smartstore.Core.Security.Permissions.Catalog.Product.Read)
            .Action("Configure", "MotorFitmentAdmin", new { area = "Admin" })
            .AsItem();

        modulesNode.Append(menuItem);
    }
}
