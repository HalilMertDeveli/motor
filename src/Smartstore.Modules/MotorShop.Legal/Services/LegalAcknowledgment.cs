using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace MotorShop.Legal.Services;

/// <summary>
/// What the customer acknowledged at the confirm step. Stored as JSON in a generic attribute of the order.
/// The client IP is not duplicated here: Smartstore already stores it in <c>Order.CustomerIp</c>.
/// </summary>
public class LegalAcknowledgment
{
    public const int CurrentVersion = 1;

    /// <summary>Generic attribute key on the order holding this record.</summary>
    public const string OrderAttributeKey = "MotorShop.Legal.Acknowledgment";

    /// <summary>Generic attribute key prefix on the order holding the rendered document snapshots.</summary>
    public const string SnapshotAttributeKeyPrefix = "MotorShop.Legal.Snapshot.";

    /// <summary>Key in <c>CheckoutState.CustomProperties</c> between the confirm post and order placement.</summary>
    public const string CheckoutStateKey = "MotorShop.Legal.Acknowledgment";

    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// <c>true</c> if the customer ticked the acknowledgment checkbox.
    /// <c>false</c> if the checkbox is not required and was left unticked (documents were still shown).
    /// </summary>
    public bool Accepted { get; set; }

    public DateTime AcceptedOnUtc { get; set; }

    public string LanguageCulture { get; set; }

    public List<LegalAcknowledgedDocument> Documents { get; set; } = [];

    /// <summary>
    /// Creates the record from the documents rendered with the cart at the confirm step.
    /// </summary>
    public static LegalAcknowledgment Create(IEnumerable<RenderedLegalDocument> confirmDocuments, bool accepted, string languageCulture, DateTime utcNow)
    {
        return new LegalAcknowledgment
        {
            Accepted = accepted,
            AcceptedOnUtc = utcNow,
            LanguageCulture = languageCulture,
            Documents = [.. (confirmDocuments ?? []).Select(x => new LegalAcknowledgedDocument
            {
                Key = x.Key,
                TopicSystemName = x.TopicSystemName,
                TopicId = x.TopicId,
                TemplateHash = x.TemplateHash,
                ConfirmRenderHash = x.RenderHash
            })]
        };
    }

    /// <summary>
    /// Adds the hashes of the snapshots rendered with the placed order.
    /// </summary>
    public void ApplyOrderDocuments(IEnumerable<RenderedLegalDocument> orderDocuments)
    {
        foreach (var rendered in orderDocuments ?? [])
        {
            var doc = Documents.FirstOrDefault(x => x.Key == rendered.Key);
            if (doc == null)
            {
                Documents.Add(doc = new LegalAcknowledgedDocument
                {
                    Key = rendered.Key,
                    TopicSystemName = rendered.TopicSystemName,
                    TopicId = rendered.TopicId
                });
            }

            doc.OrderRenderHash = rendered.RenderHash;

            // The topic may have been edited between confirm and placement (e.g. during a 3-D Secure redirect).
            // Keep the confirm-time template hash, it is what the customer saw.
            doc.TemplateHash ??= rendered.TemplateHash;
        }
    }

    public string Serialize()
        => JsonConvert.SerializeObject(this, Formatting.None);

    public static LegalAcknowledgment Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonConvert.DeserializeObject<LegalAcknowledgment>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Lowercase hex SHA-256 of the UTF-8 text. Used as the version of a topic text and of a rendered document.
    /// </summary>
    public static string ComputeHash(string text)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty)));
}

public class LegalAcknowledgedDocument
{
    /// <summary>Document key, see <see cref="LegalDocumentKeys"/>.</summary>
    public string Key { get; set; }

    public string TopicSystemName { get; set; }

    public int TopicId { get; set; }

    /// <summary>SHA-256 of the localized topic title and body (the template version).</summary>
    public string TemplateHash { get; set; }

    /// <summary>SHA-256 of the document as rendered with the cart data at the confirm step.</summary>
    public string ConfirmRenderHash { get; set; }

    /// <summary>SHA-256 of the snapshot rendered with the placed order data.</summary>
    public string OrderRenderHash { get; set; }
}

public static class LegalDocumentKeys
{
    public const string PreInformation = "PreInformation";
    public const string DistanceSales = "DistanceSales";

    public static readonly string[] All = [PreInformation, DistanceSales];
}
