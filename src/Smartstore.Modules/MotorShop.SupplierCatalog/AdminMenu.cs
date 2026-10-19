using Smartstore.Collections;
using Smartstore.Core.Content.Menus;
using Smartstore.Web.Rendering.Builders;

namespace MotorShop.SupplierCatalog;

public class AdminMenu : AdminMenuProvider
{
    protected override void BuildMenuCore(TreeNode<MenuItem> modulesNode)
    {
        var menuItem = new MenuItem().ToBuilder()
            .Text("Supplier catalog import")
            .ResKey("Plugins.FriendlyName.MotorShop.SupplierCatalog")
            .Icon("box-arrow-in-down", "bi")
            .PermissionNames(Smartstore.Core.Security.Permissions.Catalog.Product.Create)
            .Action("Configure", "SupplierCatalogAdmin", new { area = "Admin" })
            .AsItem();

        modulesNode.Append(menuItem);
    }
}
