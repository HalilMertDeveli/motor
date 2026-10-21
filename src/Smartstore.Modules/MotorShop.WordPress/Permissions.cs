using Smartstore.Core.Identity;
using Smartstore.Core.Security;

namespace MotorShop.WordPress;

/// <summary>
/// "wordpress.read": see the connection status. "wordpress.update": clear the cache, rebuild the sitemap.
/// Granted to administrators on install. Nothing here grants access to WordPress itself.
/// </summary>
public static class WordPressPermissions
{
    public const string Self = "wordpress";
    public const string Read = "wordpress.read";
    public const string Update = "wordpress.update";
}

internal class WordPressPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionRecord> GetPermissions()
        => PermissionHelper.GetPermissions(typeof(WordPressPermissions)).Select(x => new PermissionRecord { SystemName = x });

    public IEnumerable<DefaultPermissionRecord> GetDefaultPermissions() =>
    [
        new DefaultPermissionRecord
        {
            CustomerRoleSystemName = SystemCustomerRoleNames.Administrators,
            PermissionRecords = [new PermissionRecord { SystemName = WordPressPermissions.Self }]
        }
    ];
}
