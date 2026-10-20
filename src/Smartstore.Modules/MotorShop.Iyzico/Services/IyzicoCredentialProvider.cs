using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using MotorShop.Iyzico.Client;
using MotorShop.Iyzico.Configuration;
using Smartstore.Core.Configuration;
using Smartstore.Engine;

namespace MotorShop.Iyzico.Services;

public interface IIyzicoCredentialProvider
{
    /// <summary>
    /// Gets the API credentials for a store. Configuration values (environment variables) take precedence over
    /// the encrypted admin settings. Never log the result.
    /// </summary>
    Task<IyzicoCredentials> GetCredentialsAsync(int storeId);

    /// <summary>
    /// Encrypts a key entered in the admin area for storage in settings.
    /// </summary>
    string Protect(string plainText);

    /// <summary>
    /// Gets a value indicating whether the API and secret keys come from configuration.
    /// </summary>
    bool HasConfigurationOverride { get; }

    /// <summary>
    /// Disables the HMAC check of initialize, retrieve and refund response signatures. Configuration only
    /// (MotorShop:Iyzico:SkipResponseSignatureVerification=true), never an admin setting. For local mocks without signatures.
    /// </summary>
    bool SkipResponseSignatureVerification { get; }

    /// <summary>
    /// Accepts webhooks without a valid X-IYZ-SIGNATURE-V3 header. Configuration only (MotorShop:Iyzico:AllowUnsignedWebhooks=true).
    /// iyzico sends the header only after the merchant asked iyzico to enable it. A webhook never marks an order as paid
    /// by itself: it only triggers a server-side retrieve of the payment result.
    /// </summary>
    bool AllowUnsignedWebhooks { get; }
}

public class IyzicoCredentialProvider : IIyzicoCredentialProvider
{
    public const string ProductionBaseUrl = "https://api.iyzipay.com";
    public const string SandboxBaseUrl = "https://sandbox-api.iyzipay.com";
    public const string ConfigSection = "MotorShop:Iyzico";

    // Changing the purpose makes stored keys unreadable.
    const string ProtectorPurpose = "MotorShop.Iyzico.Credentials.v1";

    private readonly ISettingFactory _settingFactory;
    private readonly IConfiguration _configuration;
    private readonly IDataProtector _protector;

    public IyzicoCredentialProvider(
        ISettingFactory settingFactory,
        IApplicationContext appContext,
        IDataProtectionProvider dataProtectionProvider)
    {
        _settingFactory = settingFactory;
        _configuration = appContext.Configuration;
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
    }

    public bool HasConfigurationOverride
        => _configuration[$"{ConfigSection}:ApiKey"].HasValue() && _configuration[$"{ConfigSection}:SecretKey"].HasValue();

    public bool SkipResponseSignatureVerification
        => IsTrue($"{ConfigSection}:SkipResponseSignatureVerification");

    public bool AllowUnsignedWebhooks
        => IsTrue($"{ConfigSection}:AllowUnsignedWebhooks");

    private bool IsTrue(string key)
        => bool.TryParse(_configuration[key], out var value) && value;

    public async Task<IyzicoCredentials> GetCredentialsAsync(int storeId)
    {
        var settings = await _settingFactory.LoadSettingsAsync<IyzicoSettings>(storeId);

        string apiKey, secretKey;
        if (HasConfigurationOverride)
        {
            apiKey = _configuration[$"{ConfigSection}:ApiKey"];
            secretKey = _configuration[$"{ConfigSection}:SecretKey"];
        }
        else
        {
            apiKey = Unprotect(settings.ApiKeyProtected);
            secretKey = Unprotect(settings.SecretKeyProtected);
        }

        // A base URL from configuration is meant for local mocks and private test gateways only.
        // It cannot be set in the admin area, so an admin account alone cannot redirect the keys to another host.
        var baseUrl = _configuration[$"{ConfigSection}:BaseUrl"].NullEmpty()
            ?? (settings.UseSandbox ? SandboxBaseUrl : ProductionBaseUrl);

        return new IyzicoCredentials(apiKey, secretKey, baseUrl);
    }

    public string Protect(string plainText)
        => plainText.IsEmpty() ? null : _protector.Protect(plainText.Trim());

    private string Unprotect(string protectedText)
    {
        if (protectedText.IsEmpty())
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(protectedText);
        }
        catch (CryptographicException)
        {
            // Data protection keys were lost or rotated out. The keys must be entered again.
            return null;
        }
    }
}
