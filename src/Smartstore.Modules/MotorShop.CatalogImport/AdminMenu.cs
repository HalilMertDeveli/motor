using Smartstore.Collections;
using Smartstore.Core.Content.Menus;
using Smartstore.Web.Rendering.Builders;

namespace MotorShop.CatalogImport;

public class AdminMenu : AdminMenuProvider
{
    protected override void BuildMenuCore(TreeNode<MenuItem> modulesNode)
    {
        var menuItem = new MenuItem().ToBuilder()
            .Text("Motor Catalog Import")
            .ResKey("Plugins.FriendlyName.MotorShop.CatalogImport")
            .Icon("database-check", "bi")
            .PermissionNames(Smartstore.Core.Security.Permissions.Catalog.Product.Read)
            .Action("Index", "MotorCatalogAdmin", new { area = "Admin" })
            .AsItem();

        modulesNode.Append(menuItem);
    }
}
