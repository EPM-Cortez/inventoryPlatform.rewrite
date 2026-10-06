using Inventory.Application.Attachments;
using Inventory.Application.Common;
using Inventory.Application.SavedSearches;

namespace Inventory.Application.Tests;

public sealed class CustodyDocumentValidationTests
{
    [Fact]
    public void AttachmentValidatorUsesMagicBytesAndComputesHash()
    {
        var result = AttachmentFileValidator.Validate(
            "invoice.pdf",
            "%PDF-test"u8);

        Assert.Equal("invoice.pdf", result.FileName);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal(64, result.Sha256.Length);
    }

    [Theory]
    [InlineData("photo.pdf")]
    [InlineData("../photo.png")]
    public void AttachmentValidatorRejectsMismatchedOrUnsafeFileNames(
        string fileName)
    {
        byte[] png =
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01];

        var exception = Assert.Throws<RequestValidationException>(
            () => AttachmentFileValidator.Validate(fileName, png));

        Assert.True(
            exception.Code is
                "attachment_extension_mismatch" or
                "unsafe_attachment_filename");
    }

    [Fact]
    public void AttachmentValidatorRejectsUnrecognisedContent()
    {
        var exception = Assert.Throws<RequestValidationException>(
            () => AttachmentFileValidator.Validate(
                "payload.pdf",
                "not a document"u8));

        Assert.Equal("unsupported_attachment_type", exception.Code);
    }

    [Fact]
    public void AttachmentValidatorRejectsContentAboveTenMiB()
    {
        var content = new byte[
            Inventory.Domain.Assets.AssetAttachment.MaximumContentBytes + 1];

        var exception = Assert.Throws<RequestValidationException>(
            () => AttachmentFileValidator.Validate("large.pdf", content));

        Assert.Equal("invalid_attachment_size", exception.Code);
    }

    [Fact]
    public void SavedFilterValidatorNormalizesSupportedFilter()
    {
        var filter = ValidFilter() with
        {
            Query = "  laptop  ",
            Sort = " -updatedAtUtc ",
        };

        var result = SavedAssetFilterValidator.Validate(filter);

        Assert.Equal("laptop", result.Query);
        Assert.Equal("-updatedAtUtc", result.Sort);
    }

    [Fact]
    public void SavedFilterValidatorRejectsContradictoryHolderFilter()
    {
        var filter = ValidFilter() with
        {
            Assigned = false,
            AssignedPersonId = Guid.NewGuid(),
        };

        var exception = Assert.Throws<RequestValidationException>(
            () => SavedAssetFilterValidator.Validate(filter));

        Assert.Equal("invalid_saved_search_filter", exception.Code);
    }

    [Fact]
    public void SavedFilterValidatorRejectsNonUtcRange()
    {
        var filter = ValidFilter() with
        {
            CreatedFrom = new DateTimeOffset(
                2026,
                7,
                26,
                12,
                0,
                0,
                TimeSpan.FromHours(1)),
        };

        var exception = Assert.Throws<RequestValidationException>(
            () => SavedAssetFilterValidator.Validate(filter));

        Assert.Equal("invalid_saved_search_filter", exception.Code);
    }

    private static SavedAssetFilter ValidFilter() =>
        new(
            Query: null,
            AssetTag: null,
            SerialNumber: null,
            CategoryId: null,
            Status: null,
            Condition: null,
            LocationId: null,
            DepartmentId: null,
            AssignedPersonId: null,
            Assigned: null,
            WarrantyState: null,
            WarrantyEndsBefore: null,
            CreatedFrom: null,
            CreatedTo: null,
            LegacyId: null,
            PageSize: 50,
            Sort: "assetTag");
}
