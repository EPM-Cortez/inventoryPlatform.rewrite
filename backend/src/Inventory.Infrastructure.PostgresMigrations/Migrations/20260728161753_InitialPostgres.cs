using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Inventory.Infrastructure.PostgresMigrations.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentRequestNonces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ContextId = table.Column<Guid>(type: "uuid", nullable: false),
                    NonceHash = table.Column<string>(type: "character(44)", fixedLength: true, maxLength: 44, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRequestNonces", x => x.Id);
                    table.CheckConstraint("CK_AgentRequestNonces_Expiry", "\"ExpiresAtUtc\" > \"ReceivedAtUtc\"");
                    table.CheckConstraint("CK_AgentRequestNonces_Scope", "\"Scope\" IN ('Enrollment', 'KeyRecovery', 'CheckIn')");
                });

            migrationBuilder.CreateTable(
                name: "AssetBundles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CodeNormalized = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    NameNormalized = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetBundles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssetCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CodeNormalized = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssetTagPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Prefix = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NumericWidth = table.Column<int>(type: "integer", nullable: false),
                    NextNumber = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetTagPolicies", x => x.Id);
                    table.CheckConstraint("CK_AssetTagPolicies_Sequence", "\"NumericWidth\" BETWEEN 2 AND 12 AND \"NextNumber\" >= 1 AND length(CAST(\"NextNumber\" - 1 AS TEXT)) <= \"NumericWidth\"");
                    table.CheckConstraint("CK_AssetTagPolicies_Singleton", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CodeNormalized = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CodeNormalized = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ParentLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                    table.CheckConstraint("CK_Locations_NotOwnParent", "\"ParentLocationId\" IS NULL OR \"ParentLocationId\" <> \"Id\"");
                    table.ForeignKey(
                        name: "FK_Locations_Locations_ParentLocationId",
                        column: x => x.ParentLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExternalIdNormalized = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SchoolProfile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    QrBaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    WarrantyWarningDays = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchoolProfile", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "boolean", nullable: false),
                    LinkedPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_ArchivedDisabled", "\"ArchivedAtUtc\" IS NULL OR NOT \"IsEnabled\"");
                });

            migrationBuilder.CreateTable(
                name: "Assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetTag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AssetTagNormalized = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeviceName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Manufacturer = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Model = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SerialNumberNormalized = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LanMacAddress = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: true),
                    BiosGuid = table.Column<Guid>(type: "uuid", nullable: true),
                    Ownership = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Condition = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    PurchaseDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PurchaseCost = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Supplier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OperatingSystem = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OperatingSystemVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Windows11Compliant = table.Column<bool>(type: "boolean", nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    LegacyId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RetiredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetirementReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DisposedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisposalReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assets_AssetCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "AssetCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Assets_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleClaims_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentEnrollmentProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    TokenHash = table.Column<string>(type: "character(44)", fixedLength: true, maxLength: 44, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MaxEnrollments = table.Column<int>(type: "integer", nullable: false),
                    EnrolledCount = table.Column<int>(type: "integer", nullable: false),
                    RequireTpm = table.Column<bool>(type: "boolean", nullable: false),
                    AllowedHostnameSuffix = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentEnrollmentProfiles", x => x.Id);
                    table.CheckConstraint("CK_AgentEnrollmentProfiles_EnrollmentCount", "\"EnrolledCount\" >= 0 AND \"MaxEnrollments\" >= 1 AND \"MaxEnrollments\" <= 5000 AND \"EnrolledCount\" <= \"MaxEnrollments\"");
                    table.CheckConstraint("CK_AgentEnrollmentProfiles_Expiry", "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_AgentEnrollmentProfiles_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetBulkChangeJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OperationJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    ItemsJson = table.Column<string>(type: "character varying(32000)", maxLength: 32000, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AssetCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CommittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetBulkChangeJobs", x => x.Id);
                    table.CheckConstraint("CK_AssetBulkChangeJobs_AssetCount", "\"AssetCount\" BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_AssetBulkChangeJobs_Commit", "(\"Status\" = 'Previewed' AND \"CommittedAtUtc\" IS NULL) OR (\"Status\" = 'Committed' AND \"CommittedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_AssetBulkChangeJobs_Expiry", "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_AssetBulkChangeJobs_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetImportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    FileHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    SnapshotHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TotalRows = table.Column<int>(type: "integer", nullable: false),
                    ValidRows = table.Column<int>(type: "integer", nullable: false),
                    ErrorRows = table.Column<int>(type: "integer", nullable: false),
                    CreateRows = table.Column<int>(type: "integer", nullable: false),
                    UpdateRows = table.Column<int>(type: "integer", nullable: false),
                    ValidatedRowsJson = table.Column<string>(type: "text", nullable: false),
                    ErrorsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CommittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CommittedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CommittedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CommittedCreateRows = table.Column<int>(type: "integer", nullable: false),
                    CommittedUpdateRows = table.Column<int>(type: "integer", nullable: false),
                    CommittedUnchangedRows = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetImportJobs", x => x.Id);
                    table.CheckConstraint("CK_AssetImportJobs_CommitCounts", "\"CommittedCreateRows\" >= 0 AND \"CommittedUpdateRows\" >= 0 AND \"CommittedUnchangedRows\" >= 0 AND ((\"Status\" = 'Committed' AND \"CommittedCreateRows\" + \"CommittedUpdateRows\" + \"CommittedUnchangedRows\" = \"ValidRows\") OR (\"Status\" <> 'Committed' AND \"CommittedCreateRows\" = 0 AND \"CommittedUpdateRows\" = 0 AND \"CommittedUnchangedRows\" = 0))");
                    table.CheckConstraint("CK_AssetImportJobs_CommitState", "(\"Status\" = 'Committed' AND \"CommittedAtUtc\" IS NOT NULL AND \"CommittedByUserId\" IS NOT NULL AND \"CommittedByDisplayName\" IS NOT NULL) OR (\"Status\" <> 'Committed' AND \"CommittedAtUtc\" IS NULL AND \"CommittedByUserId\" IS NULL AND \"CommittedByDisplayName\" IS NULL)");
                    table.CheckConstraint("CK_AssetImportJobs_Counts", "\"TotalRows\" >= 0 AND \"ValidRows\" >= 0 AND \"ErrorRows\" >= 0 AND \"CreateRows\" >= 0 AND \"UpdateRows\" >= 0 AND \"ValidRows\" + \"ErrorRows\" = \"TotalRows\" AND \"CreateRows\" + \"UpdateRows\" = \"ValidRows\"");
                    table.ForeignKey(
                        name: "FK_AssetImportJobs_Users_CommittedByUserId",
                        column: x => x.CommittedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetImportJobs_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DetailsJson = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Client = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaverCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeavingOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaverCases", x => x.Id);
                    table.CheckConstraint("CK_LeaverCases_Lifecycle", "(\"Status\" = 'Open' AND \"CompletedAtUtc\" IS NULL AND \"CompletedByUserId\" IS NULL AND \"CancelledAtUtc\" IS NULL AND \"CancelledByUserId\" IS NULL) OR (\"Status\" = 'Completed' AND \"CompletedAtUtc\" IS NOT NULL AND \"CompletedByUserId\" IS NOT NULL AND \"CancelledAtUtc\" IS NULL AND \"CancelledByUserId\" IS NULL) OR (\"Status\" = 'Cancelled' AND \"CancelledAtUtc\" IS NOT NULL AND \"CancelledByUserId\" IS NOT NULL AND \"CompletedAtUtc\" IS NULL AND \"CompletedByUserId\" IS NULL) OR (\"Status\" = 'Rehired' AND \"CompletedAtUtc\" IS NOT NULL AND \"CompletedByUserId\" IS NOT NULL AND \"CancelledAtUtc\" IS NULL AND \"CancelledByUserId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_LeaverCases_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaverCases_Users_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaverCases_Users_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaverCases_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReferenceDataImportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Resource = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    FileHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    SnapshotHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TotalRows = table.Column<int>(type: "integer", nullable: false),
                    ValidRows = table.Column<int>(type: "integer", nullable: false),
                    ErrorRows = table.Column<int>(type: "integer", nullable: false),
                    CreateRows = table.Column<int>(type: "integer", nullable: false),
                    UpdateRows = table.Column<int>(type: "integer", nullable: false),
                    ValidatedRowsJson = table.Column<string>(type: "text", nullable: false),
                    ErrorsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CommittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CommittedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CommittedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CommittedCreateRows = table.Column<int>(type: "integer", nullable: false),
                    CommittedUpdateRows = table.Column<int>(type: "integer", nullable: false),
                    CommittedUnchangedRows = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferenceDataImportJobs", x => x.Id);
                    table.CheckConstraint("CK_ReferenceDataImportJobs_CommitCounts", "\"CommittedCreateRows\" >= 0 AND \"CommittedUpdateRows\" >= 0 AND \"CommittedUnchangedRows\" >= 0 AND ((\"Status\" = 'Committed' AND \"CommittedCreateRows\" + \"CommittedUpdateRows\" + \"CommittedUnchangedRows\" = \"ValidRows\") OR (\"Status\" <> 'Committed' AND \"CommittedCreateRows\" = 0 AND \"CommittedUpdateRows\" = 0 AND \"CommittedUnchangedRows\" = 0))");
                    table.CheckConstraint("CK_ReferenceDataImportJobs_CommitState", "(\"Status\" = 'Committed' AND \"CommittedAtUtc\" IS NOT NULL AND \"CommittedByUserId\" IS NOT NULL AND \"CommittedByDisplayName\" IS NOT NULL) OR (\"Status\" <> 'Committed' AND \"CommittedAtUtc\" IS NULL AND \"CommittedByUserId\" IS NULL AND \"CommittedByDisplayName\" IS NULL)");
                    table.CheckConstraint("CK_ReferenceDataImportJobs_Counts", "\"TotalRows\" >= 0 AND \"ValidRows\" >= 0 AND \"ErrorRows\" >= 0 AND \"CreateRows\" >= 0 AND \"UpdateRows\" >= 0 AND \"ValidRows\" + \"ErrorRows\" = \"TotalRows\" AND \"CreateRows\" + \"UpdateRows\" = \"ValidRows\"");
                    table.ForeignKey(
                        name: "FK_ReferenceDataImportJobs_Users_CommittedByUserId",
                        column: x => x.CommittedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReferenceDataImportJobs_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SavedAssetSearches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameNormalized = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    FilterJson = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedAssetSearches", x => x.Id);
                    table.CheckConstraint("CK_SavedAssetSearches_ArchivedDefault", "\"ArchivedAtUtc\" IS NULL OR NOT \"IsDefault\"");
                    table.CheckConstraint("CK_SavedAssetSearches_FilterSize", "length(\"FilterJson\") BETWEEN 2 AND 16000");
                    table.CheckConstraint("CK_SavedAssetSearches_SchemaVersion", "\"SchemaVersion\" = 1");
                    table.ForeignKey(
                        name: "FK_SavedAssetSearches_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StocktakeSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ScopeLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScopeCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StocktakeSessions", x => x.Id);
                    table.CheckConstraint("CK_StocktakeSessions_Lifecycle", "(\"Status\" = 'InProgress' AND \"CompletedAtUtc\" IS NULL AND \"CompletedByUserId\" IS NULL AND \"CompletedByDisplayName\" IS NULL AND \"CancelledAtUtc\" IS NULL AND \"CancelledByUserId\" IS NULL AND \"CancelledByDisplayName\" IS NULL AND \"CancellationReason\" IS NULL) OR (\"Status\" = 'Completed' AND \"CompletedAtUtc\" IS NOT NULL AND \"CompletedByUserId\" IS NOT NULL AND \"CompletedByDisplayName\" IS NOT NULL AND \"CancelledAtUtc\" IS NULL AND \"CancelledByUserId\" IS NULL AND \"CancelledByDisplayName\" IS NULL AND \"CancellationReason\" IS NULL) OR (\"Status\" = 'Cancelled' AND \"CompletedAtUtc\" IS NULL AND \"CompletedByUserId\" IS NULL AND \"CompletedByDisplayName\" IS NULL AND \"CancelledAtUtc\" IS NOT NULL AND \"CancelledByUserId\" IS NOT NULL AND \"CancelledByDisplayName\" IS NOT NULL AND \"CancellationReason\" IS NOT NULL)");
                    table.CheckConstraint("CK_StocktakeSessions_Scope", "(\"Scope\" = 'All' AND \"ScopeLocationId\" IS NULL AND \"ScopeCategoryId\" IS NULL) OR (\"Scope\" = 'Location' AND \"ScopeLocationId\" IS NOT NULL AND \"ScopeCategoryId\" IS NULL) OR (\"Scope\" = 'Category' AND \"ScopeLocationId\" IS NULL AND \"ScopeCategoryId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_StocktakeSessions_AssetCategories_ScopeCategoryId",
                        column: x => x.ScopeCategoryId,
                        principalTable: "AssetCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeSessions_Locations_ScopeLocationId",
                        column: x => x.ScopeLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeSessions_Users_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeSessions_Users_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeSessions_Users_StartedByUserId",
                        column: x => x.StartedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserClaims_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_UserLogins_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_UserTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TargetReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AssignedCondition = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AssignedLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignmentNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ExpectedReturnOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ReturnedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReturnedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReturnedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReturnCondition = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ReturnLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReturnNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetAssignments", x => x.Id);
                    table.CheckConstraint("CK_AssetAssignments_ExactlyOneTarget", "(\"PersonId\" IS NOT NULL AND \"DepartmentId\" IS NULL) OR (\"PersonId\" IS NULL AND \"DepartmentId\" IS NOT NULL)");
                    table.CheckConstraint("CK_AssetAssignments_ExpectedReturnOn", "\"ExpectedReturnOn\" IS NULL OR \"ExpectedReturnOn\" > '0001-01-01'");
                    table.CheckConstraint("CK_AssetAssignments_ReturnedFields", "(\"ReturnedAtUtc\" IS NULL AND \"ReturnedByUserId\" IS NULL) OR (\"ReturnedAtUtc\" IS NOT NULL AND \"ReturnedByUserId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_AssetAssignments_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetAssignments_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetAssignments_Locations_AssignedLocationId",
                        column: x => x.AssignedLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetAssignments_Locations_ReturnLocationId",
                        column: x => x.ReturnLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetAssignments_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetAssignments_Users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetAssignments_Users_ReturnedByUserId",
                        column: x => x.ReturnedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArchivedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ArchivedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetAttachments", x => x.Id);
                    table.CheckConstraint("CK_AssetAttachments_Archive", "(\"ArchivedAtUtc\" IS NULL AND \"ArchivedByUserId\" IS NULL) OR (\"ArchivedAtUtc\" IS NOT NULL AND \"ArchivedByUserId\" IS NOT NULL)");
                    table.CheckConstraint("CK_AssetAttachments_ContentSize", "\"SizeBytes\" >= 1 AND \"SizeBytes\" <= 10485760 AND length(\"Content\") = \"SizeBytes\"");
                    table.CheckConstraint("CK_AssetAttachments_FileTypeAndHash", "\"ContentType\" IN ('application/pdf', 'image/jpeg', 'image/png') AND length(\"Sha256\") = 64");
                    table.ForeignKey(
                        name: "FK_AssetAttachments_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetAttachments_Users_ArchivedByUserId",
                        column: x => x.ArchivedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetAttachments_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetBundleMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OtherRole = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    AddedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RemovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetBundleMembers", x => x.Id);
                    table.CheckConstraint("CK_AssetBundleMembers_Lifecycle", "(\"RemovedAtUtc\" IS NULL) OR (NOT \"IsActive\")");
                    table.CheckConstraint("CK_AssetBundleMembers_OtherRole", "(\"Role\" = 'Other' AND \"OtherRole\" IS NOT NULL) OR (\"Role\" <> 'Other' AND \"OtherRole\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_AssetBundleMembers_AssetBundles_BundleId",
                        column: x => x.BundleId,
                        principalTable: "AssetBundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetBundleMembers_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DetailsJson = table.Column<string>(type: "text", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetEvents_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetEvents_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QrIdentifiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProtectedToken = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    TokenVersion = table.Column<int>(type: "integer", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IssuedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssuedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RevocationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QrIdentifiers", x => x.Id);
                    table.CheckConstraint("CK_QrIdentifiers_RevocationFields", "(\"RevokedAtUtc\" IS NULL AND \"RevokedByUserId\" IS NULL) OR (\"RevokedAtUtc\" IS NOT NULL AND \"RevokedByUserId\" IS NOT NULL)");
                    table.CheckConstraint("CK_QrIdentifiers_TokenVersion", "\"TokenVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_QrIdentifiers_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QrIdentifiers_Users_IssuedByUserId",
                        column: x => x.IssuedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QrIdentifiers_Users_RevokedByUserId",
                        column: x => x.RevokedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WarrantyCoverages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CoverageType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarrantyCoverages", x => x.Id);
                    table.CheckConstraint("CK_WarrantyCoverages_ArchivedPrimary", "\"ArchivedAtUtc\" IS NULL OR NOT \"IsPrimary\"");
                    table.CheckConstraint("CK_WarrantyCoverages_DateRange", "\"StartDate\" IS NULL OR \"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
                    table.ForeignKey(
                        name: "FK_WarrantyCoverages_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ManagedAgents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeviceIdNormalized = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PublicKeyPem = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    KeyThumbprintSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    KeyProtection = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Hostname = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    HostnameNormalized = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SerialNumberNormalized = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BiosUuid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BiosUuidNormalized = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Manufacturer = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Model = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    MatchState = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    MatchReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    EnrolledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastCheckInAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCollectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAgentVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    LastSeenIpAddress = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    KeyReplacedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    KeyReplacementCount = table.Column<int>(type: "integer", nullable: false),
                    CheckInBurstWindowStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckInBurstCount = table.Column<int>(type: "integer", nullable: false),
                    CheckInQuotaWindowStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckInQuotaCount = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedAgents", x => x.Id);
                    table.CheckConstraint("CK_ManagedAgents_CheckInCapacity", "\"CheckInBurstCount\" >= 0 AND \"CheckInQuotaCount\" >= 0");
                    table.CheckConstraint("CK_ManagedAgents_KeyReplacementCount", "\"KeyReplacementCount\" >= 0");
                    table.CheckConstraint("CK_ManagedAgents_LastSequence", "\"LastSequence\" >= 0");
                    table.CheckConstraint("CK_ManagedAgents_Revocation", "(\"RevokedAtUtc\" IS NULL AND \"RevokedReason\" IS NULL) OR (\"RevokedAtUtc\" IS NOT NULL AND \"RevokedReason\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ManagedAgents_AgentEnrollmentProfiles_EnrollmentProfileId",
                        column: x => x.EnrollmentProfileId,
                        principalTable: "AgentEnrollmentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManagedAgents_Assets_LinkedAssetId",
                        column: x => x.LinkedAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LegacyReconciliationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetImportJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportFingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ReportJson = table.Column<string>(type: "text", nullable: false),
                    TargetAssetCount = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyReconciliationRuns", x => x.Id);
                    table.CheckConstraint("CK_LegacyReconciliationRuns_TargetAssetCount", "\"TargetAssetCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_LegacyReconciliationRuns_AssetImportJobs_AssetImportJobId",
                        column: x => x.AssetImportJobId,
                        principalTable: "AssetImportJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LegacyReconciliationRuns_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StocktakeExpectedAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetTag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeviceName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CategoryName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ExpectedStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ExpectedCondition = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ExpectedLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpectedLocationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ExpectedLocationName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StocktakeExpectedAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StocktakeExpectedAssets_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeExpectedAssets_StocktakeSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "StocktakeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StocktakeObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetTag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeviceName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CategoryName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ObservedLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObservedLocationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ObservedLocationName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ObservedCondition = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ObservedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObservedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StocktakeObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StocktakeObservations_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeObservations_Locations_ObservedLocationId",
                        column: x => x.ObservedLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeObservations_StocktakeSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "StocktakeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StocktakeObservations_Users_ObservedByUserId",
                        column: x => x.ObservedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AgentKeyRecoveryAuthorizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character(44)", fixedLength: true, maxLength: 44, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RequireTpm = table.Column<bool>(type: "boolean", nullable: false),
                    ExpectedAgentVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsumedKeyThumbprintSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentKeyRecoveryAuthorizations", x => x.Id);
                    table.CheckConstraint("CK_AgentKeyRecoveryAuthorizations_ClosedState", "NOT (\"ConsumedAtUtc\" IS NOT NULL AND \"RevokedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_AgentKeyRecoveryAuthorizations_Consumption", "(\"ConsumedAtUtc\" IS NULL AND \"ConsumedKeyThumbprintSha256\" IS NULL) OR (\"ConsumedAtUtc\" IS NOT NULL AND \"ConsumedKeyThumbprintSha256\" IS NOT NULL)");
                    table.CheckConstraint("CK_AgentKeyRecoveryAuthorizations_Expiry", "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
                    table.CheckConstraint("CK_AgentKeyRecoveryAuthorizations_Revocation", "(\"RevokedAtUtc\" IS NULL AND \"RevokedByUserId\" IS NULL AND \"RevokedReason\" IS NULL) OR (\"RevokedAtUtc\" IS NOT NULL AND \"RevokedByUserId\" IS NOT NULL AND \"RevokedReason\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_AgentKeyRecoveryAuthorizations_ManagedAgents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "ManagedAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AgentKeyRecoveryAuthorizations_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AgentKeyRecoveryAuthorizations_Users_RevokedByUserId",
                        column: x => x.RevokedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ManagedAgentSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    CollectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BodySha256 = table.Column<string>(type: "character(44)", fixedLength: true, maxLength: 44, nullable: false),
                    ContentSha256 = table.Column<string>(type: "character(44)", fixedLength: true, maxLength: 44, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: true),
                    Hostname = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BiosUuid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AgentVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedAgentSnapshots", x => x.Id);
                    table.CheckConstraint("CK_ManagedAgentSnapshots_Sequence", "\"Sequence\" >= 1");
                    table.ForeignKey(
                        name: "FK_ManagedAgentSnapshots_ManagedAgents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "ManagedAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LegacyReconciliationSignOffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReconciliationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReportFingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    SignedExceptionsJson = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    SignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SignedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyReconciliationSignOffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegacyReconciliationSignOffs_LegacyReconciliationRuns_Recon~",
                        column: x => x.ReconciliationRunId,
                        principalTable: "LegacyReconciliationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LegacyReconciliationSignOffs_Users_SignedByUserId",
                        column: x => x.SignedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AssetTagPolicies",
                columns: new[] { "Id", "NextNumber", "NumericWidth", "Prefix", "Version" },
                values: new object[] { 1, 1L, 6, "AST-", new Guid("00000000-0000-0000-0000-000000000701") });

            migrationBuilder.CreateIndex(
                name: "IX_AgentEnrollmentProfiles_CreatedByUserId",
                table: "AgentEnrollmentProfiles",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentEnrollmentProfiles_ExpiresAtUtc",
                table: "AgentEnrollmentProfiles",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AgentEnrollmentProfiles_RevokedAtUtc",
                table: "AgentEnrollmentProfiles",
                column: "RevokedAtUtc");

            migrationBuilder.CreateIndex(
                name: "UX_AgentEnrollmentProfiles_TokenHash",
                table: "AgentEnrollmentProfiles",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentKeyRecoveryAuthorizations_CreatedByUserId",
                table: "AgentKeyRecoveryAuthorizations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentKeyRecoveryAuthorizations_ExpiresAtUtc",
                table: "AgentKeyRecoveryAuthorizations",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AgentKeyRecoveryAuthorizations_RevokedByUserId",
                table: "AgentKeyRecoveryAuthorizations",
                column: "RevokedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_AgentKeyRecoveryAuthorizations_PendingAgent",
                table: "AgentKeyRecoveryAuthorizations",
                column: "AgentId",
                unique: true,
                filter: "\"ConsumedAtUtc\" IS NULL AND \"RevokedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_AgentKeyRecoveryAuthorizations_TokenHash",
                table: "AgentKeyRecoveryAuthorizations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentRequestNonces_ExpiresAtUtc",
                table: "AgentRequestNonces",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "UX_AgentRequestNonces_ScopeContextNonce",
                table: "AgentRequestNonces",
                columns: new[] { "Scope", "ContextId", "NonceHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_AssetId_AssignedAtUtc",
                table: "AssetAssignments",
                columns: new[] { "AssetId", "AssignedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_AssignedByUserId",
                table: "AssetAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_AssignedLocationId",
                table: "AssetAssignments",
                column: "AssignedLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_DepartmentId_ReturnedAtUtc",
                table: "AssetAssignments",
                columns: new[] { "DepartmentId", "ReturnedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_ExpectedReturnOn_ReturnedAtUtc",
                table: "AssetAssignments",
                columns: new[] { "ExpectedReturnOn", "ReturnedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_PersonId_ReturnedAtUtc",
                table: "AssetAssignments",
                columns: new[] { "PersonId", "ReturnedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_ReturnedByUserId",
                table: "AssetAssignments",
                column: "ReturnedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_ReturnLocationId",
                table: "AssetAssignments",
                column: "ReturnLocationId");

            migrationBuilder.CreateIndex(
                name: "UX_AssetAssignments_OpenAsset",
                table: "AssetAssignments",
                column: "AssetId",
                unique: true,
                filter: "\"ReturnedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAttachments_ArchivedByUserId",
                table: "AssetAttachments",
                column: "ArchivedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAttachments_AssetId_ArchivedAtUtc_CreatedAtUtc",
                table: "AssetAttachments",
                columns: new[] { "AssetId", "ArchivedAtUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAttachments_AssetId_Sha256",
                table: "AssetAttachments",
                columns: new[] { "AssetId", "Sha256" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAttachments_CreatedByUserId",
                table: "AssetAttachments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetBulkChangeJobs_CreatedByUserId",
                table: "AssetBulkChangeJobs",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetBulkChangeJobs_Fingerprint",
                table: "AssetBulkChangeJobs",
                column: "Fingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_AssetBulkChangeJobs_Status_ExpiresAtUtc",
                table: "AssetBulkChangeJobs",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetBundleMembers_AssetId_Active",
                table: "AssetBundleMembers",
                column: "AssetId",
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "UX_AssetBundleMembers_BundleAsset",
                table: "AssetBundleMembers",
                columns: new[] { "BundleId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AssetBundles_CodeNormalized",
                table: "AssetBundles",
                column: "CodeNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AssetBundles_NameNormalized",
                table: "AssetBundles",
                column: "NameNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AssetCategories_CodeNormalized",
                table: "AssetCategories",
                column: "CodeNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetEvents_ActorUserId",
                table: "AssetEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetEvents_AssetId_OccurredAtUtc",
                table: "AssetEvents",
                columns: new[] { "AssetId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetImportJobs_CommittedByUserId",
                table: "AssetImportJobs",
                column: "CommittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetImportJobs_CreatedAtUtc",
                table: "AssetImportJobs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AssetImportJobs_CreatedByUserId",
                table: "AssetImportJobs",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetImportJobs_Status_CreatedAtUtc",
                table: "AssetImportJobs",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_CategoryId",
                table: "Assets",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_LegacyId",
                table: "Assets",
                column: "LegacyId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_LocationId",
                table: "Assets",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Status",
                table: "Assets",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_Assets_AssetTagNormalized",
                table: "Assets",
                column: "AssetTagNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Assets_SerialNumberNormalized",
                table: "Assets",
                column: "SerialNumberNormalized",
                unique: true,
                filter: "\"SerialNumberNormalized\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action_OccurredAtUtc",
                table: "AuditLogs",
                columns: new[] { "Action", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActorUserId",
                table: "AuditLogs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OccurredAtUtc",
                table: "AuditLogs",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_IsActive_Name",
                table: "Departments",
                columns: new[] { "IsActive", "Name" });

            migrationBuilder.CreateIndex(
                name: "UX_Departments_CodeNormalized",
                table: "Departments",
                column: "CodeNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaverCases_CancelledByUserId",
                table: "LeaverCases",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaverCases_CompletedByUserId",
                table: "LeaverCases",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaverCases_CreatedByUserId",
                table: "LeaverCases",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaverCases_Status_LeavingOn",
                table: "LeaverCases",
                columns: new[] { "Status", "LeavingOn" });

            migrationBuilder.CreateIndex(
                name: "UX_LeaverCases_OpenPerson",
                table: "LeaverCases",
                column: "PersonId",
                unique: true,
                filter: "\"Status\" = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyReconciliationRuns_AssetImportJobId",
                table: "LegacyReconciliationRuns",
                column: "AssetImportJobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegacyReconciliationRuns_CreatedByUserId",
                table: "LegacyReconciliationRuns",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyReconciliationRuns_ReportFingerprint",
                table: "LegacyReconciliationRuns",
                column: "ReportFingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyReconciliationSignOffs_ReconciliationRunId",
                table: "LegacyReconciliationSignOffs",
                column: "ReconciliationRunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegacyReconciliationSignOffs_SignedByUserId",
                table: "LegacyReconciliationSignOffs",
                column: "SignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_ParentLocationId",
                table: "Locations",
                column: "ParentLocationId");

            migrationBuilder.CreateIndex(
                name: "UX_Locations_CodeNormalized",
                table: "Locations",
                column: "CodeNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManagedAgents_EnrollmentProfileId",
                table: "ManagedAgents",
                column: "EnrollmentProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ManagedAgents_HostnameNormalized",
                table: "ManagedAgents",
                column: "HostnameNormalized");

            migrationBuilder.CreateIndex(
                name: "IX_ManagedAgents_LastCheckInAtUtc",
                table: "ManagedAgents",
                column: "LastCheckInAtUtc");

            migrationBuilder.CreateIndex(
                name: "UX_ManagedAgents_DeviceIdNormalized",
                table: "ManagedAgents",
                column: "DeviceIdNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ManagedAgents_KeyThumbprint",
                table: "ManagedAgents",
                column: "KeyThumbprintSha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ManagedAgents_LinkedAssetId",
                table: "ManagedAgents",
                column: "LinkedAssetId",
                unique: true,
                filter: "\"LinkedAssetId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ManagedAgentSnapshots_AgentId_ReceivedAtUtc",
                table: "ManagedAgentSnapshots",
                columns: new[] { "AgentId", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ManagedAgentSnapshots_AgentSequence",
                table: "ManagedAgentSnapshots",
                columns: new[] { "AgentId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ManagedAgentSnapshots_AgentSnapshot",
                table: "ManagedAgentSnapshots",
                columns: new[] { "AgentId", "SnapshotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_People_IsActive_DisplayName",
                table: "People",
                columns: new[] { "IsActive", "DisplayName" });

            migrationBuilder.CreateIndex(
                name: "UX_People_ExternalIdNormalized",
                table: "People",
                column: "ExternalIdNormalized",
                unique: true,
                filter: "\"ExternalIdNormalized\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_QrIdentifiers_IssuedByUserId",
                table: "QrIdentifiers",
                column: "IssuedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QrIdentifiers_RevokedByUserId",
                table: "QrIdentifiers",
                column: "RevokedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_QrIdentifiers_ActiveAsset",
                table: "QrIdentifiers",
                column: "AssetId",
                unique: true,
                filter: "\"RevokedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_QrIdentifiers_AssetVersion",
                table: "QrIdentifiers",
                columns: new[] { "AssetId", "TokenVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_QrIdentifiers_TokenHash",
                table: "QrIdentifiers",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReferenceDataImportJobs_CommittedByUserId",
                table: "ReferenceDataImportJobs",
                column: "CommittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferenceDataImportJobs_CreatedAtUtc",
                table: "ReferenceDataImportJobs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ReferenceDataImportJobs_CreatedByUserId",
                table: "ReferenceDataImportJobs",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferenceDataImportJobs_Resource_Status_CreatedAtUtc",
                table: "ReferenceDataImportJobs",
                columns: new[] { "Resource", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleClaims_RoleId",
                table: "RoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "Roles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SavedAssetSearches_ActiveName",
                table: "SavedAssetSearches",
                columns: new[] { "UserId", "NameNormalized" },
                unique: true,
                filter: "\"ArchivedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_SavedAssetSearches_DefaultUser",
                table: "SavedAssetSearches",
                column: "UserId",
                unique: true,
                filter: "\"IsDefault\" AND \"ArchivedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeExpectedAssets_AssetId",
                table: "StocktakeExpectedAssets",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeExpectedAssets_SessionId_AssetTag",
                table: "StocktakeExpectedAssets",
                columns: new[] { "SessionId", "AssetTag" });

            migrationBuilder.CreateIndex(
                name: "UX_StocktakeExpectedAssets_SessionAsset",
                table: "StocktakeExpectedAssets",
                columns: new[] { "SessionId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeObservations_AssetId",
                table: "StocktakeObservations",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeObservations_ObservedByUserId",
                table: "StocktakeObservations",
                column: "ObservedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeObservations_ObservedLocationId",
                table: "StocktakeObservations",
                column: "ObservedLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeObservations_SessionId_ObservedAtUtc",
                table: "StocktakeObservations",
                columns: new[] { "SessionId", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_StocktakeObservations_SessionAsset",
                table: "StocktakeObservations",
                columns: new[] { "SessionId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeSessions_CancelledByUserId",
                table: "StocktakeSessions",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeSessions_CompletedByUserId",
                table: "StocktakeSessions",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeSessions_ScopeCategoryId",
                table: "StocktakeSessions",
                column: "ScopeCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeSessions_ScopeLocationId",
                table: "StocktakeSessions",
                column: "ScopeLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeSessions_StartedByUserId",
                table: "StocktakeSessions",
                column: "StartedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeSessions_Status_StartedAtUtc",
                table: "StocktakeSessions",
                columns: new[] { "Status", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserClaims_UserId",
                table: "UserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLogins_UserId",
                table: "UserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "Users",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_Users_ArchivedAtUtc",
                table: "Users",
                column: "ArchivedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Users_IsEnabled",
                table: "Users",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "Users",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarrantyCoverages_AssetId_EndDate",
                table: "WarrantyCoverages",
                columns: new[] { "AssetId", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "UX_WarrantyCoverages_ActivePrimary",
                table: "WarrantyCoverages",
                column: "AssetId",
                unique: true,
                filter: "\"IsPrimary\" AND \"ArchivedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentKeyRecoveryAuthorizations");

            migrationBuilder.DropTable(
                name: "AgentRequestNonces");

            migrationBuilder.DropTable(
                name: "AssetAssignments");

            migrationBuilder.DropTable(
                name: "AssetAttachments");

            migrationBuilder.DropTable(
                name: "AssetBulkChangeJobs");

            migrationBuilder.DropTable(
                name: "AssetBundleMembers");

            migrationBuilder.DropTable(
                name: "AssetEvents");

            migrationBuilder.DropTable(
                name: "AssetTagPolicies");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "LeaverCases");

            migrationBuilder.DropTable(
                name: "LegacyReconciliationSignOffs");

            migrationBuilder.DropTable(
                name: "ManagedAgentSnapshots");

            migrationBuilder.DropTable(
                name: "QrIdentifiers");

            migrationBuilder.DropTable(
                name: "ReferenceDataImportJobs");

            migrationBuilder.DropTable(
                name: "RoleClaims");

            migrationBuilder.DropTable(
                name: "SavedAssetSearches");

            migrationBuilder.DropTable(
                name: "SchoolProfile");

            migrationBuilder.DropTable(
                name: "StocktakeExpectedAssets");

            migrationBuilder.DropTable(
                name: "StocktakeObservations");

            migrationBuilder.DropTable(
                name: "UserClaims");

            migrationBuilder.DropTable(
                name: "UserLogins");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "UserTokens");

            migrationBuilder.DropTable(
                name: "WarrantyCoverages");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "AssetBundles");

            migrationBuilder.DropTable(
                name: "People");

            migrationBuilder.DropTable(
                name: "LegacyReconciliationRuns");

            migrationBuilder.DropTable(
                name: "ManagedAgents");

            migrationBuilder.DropTable(
                name: "StocktakeSessions");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "AssetImportJobs");

            migrationBuilder.DropTable(
                name: "AgentEnrollmentProfiles");

            migrationBuilder.DropTable(
                name: "Assets");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "AssetCategories");

            migrationBuilder.DropTable(
                name: "Locations");
        }
    }
}
