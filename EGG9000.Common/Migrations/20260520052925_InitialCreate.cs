using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
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
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AutomationLogs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: true),
                    EndTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Skipped = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationLogs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Contracts",
                columns: table => new
                {
                    ID = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    GoodUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    egg = table.Column<string>(type: "text", nullable: true),
                    goals = table.Column<string>(type: "text", nullable: true),
                    coop_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    MaxUsers = table.Column<int>(type: "integer", nullable: false),
                    max_boosts = table.Column<int>(type: "integer", nullable: false),
                    max_soul_eggs = table.Column<double>(type: "double precision", nullable: false),
                    min_client_version = table.Column<int>(type: "integer", nullable: false),
                    debug = table.Column<bool>(type: "boolean", nullable: false),
                    length_seconds = table.Column<double>(type: "double precision", nullable: false),
                    cc_only = table.Column<bool>(type: "boolean", nullable: false),
                    _response = table.Column<string>(type: "text", nullable: true),
                    HadTwoRewards = table.Column<bool>(type: "boolean", nullable: false),
                    egg_value = table.Column<double>(type: "double precision", nullable: false),
                    Rewards = table.Column<string>(type: "text", nullable: true),
                    P2 = table.Column<int>(type: "integer", nullable: false),
                    P4 = table.Column<int>(type: "integer", nullable: false),
                    P6 = table.Column<double>(type: "double precision", nullable: false),
                    P7 = table.Column<double>(type: "double precision", nullable: false),
                    P11 = table.Column<int>(type: "integer", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contracts", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "CustomEggs",
                columns: table => new
                {
                    Identifier = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Value = table.Column<double>(type: "double precision", nullable: false),
                    _iconBytes = table.Column<byte[]>(type: "bytea", nullable: true),
                    _modifiersBytes = table.Column<byte[]>(type: "bytea", nullable: true),
                    EmojiName = table.Column<string>(type: "text", nullable: true),
                    EmojiId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Released = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomEggs", x => x.Identifier);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventCustomizations",
                columns: table => new
                {
                    Type = table.Column<string>(type: "text", nullable: false),
                    Color = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Fields = table.Column<string>(type: "text", nullable: true),
                    ThumbnailURL = table.Column<string>(type: "text", nullable: true),
                    Emoji = table.Column<string>(type: "text", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    _settings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventCustomizations", x => x.Type);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    Identifier = table.Column<string>(type: "text", nullable: true),
                    Ends = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: true),
                    Multiplier = table.Column<double>(type: "double precision", nullable: false),
                    Subtitle = table.Column<string>(type: "text", nullable: true),
                    MessageIds = table.Column<string>(type: "text", nullable: true),
                    Ended = table.Column<bool>(type: "boolean", nullable: false),
                    CcOnly = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ExpiringShells",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    Expires = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Price = table.Column<long>(type: "bigint", nullable: false),
                    AssetType = table.Column<int>(type: "integer", nullable: false),
                    Identifier = table.Column<string>(type: "text", nullable: true),
                    Json = table.Column<string>(type: "text", nullable: true),
                    MessageIds = table.Column<string>(type: "text", nullable: true),
                    Archived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpiringShells", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "FAQTopics",
                columns: table => new
                {
                    InternalId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    _keywords = table.Column<string>(type: "text", nullable: true),
                    Weight = table.Column<int>(type: "integer", nullable: false),
                    Explanation = table.Column<string>(type: "text", nullable: true),
                    StaffOnly = table.Column<bool>(type: "boolean", nullable: false),
                    PalaceOnly = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByIdString = table.Column<string>(type: "text", nullable: true),
                    CreatedById = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    GuildName = table.Column<string>(type: "text", nullable: true),
                    GuildIdString = table.Column<string>(type: "text", nullable: true),
                    GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    _subscribedGuildIds = table.Column<string>(type: "text", nullable: true),
                    EmbedColorHex = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FAQTopics", x => x.InternalId);
                });

            migrationBuilder.CreateTable(
                name: "GlobalLeaderboardCoops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    ContractID = table.Column<string>(type: "text", nullable: true),
                    Checked = table.Column<bool>(type: "boolean", nullable: false),
                    CheckFailed = table.Column<bool>(type: "boolean", nullable: false),
                    DegreeOfSeperation = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalLeaderboardCoops", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "GlobalLeaderboardUsers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    NeedsUpdate = table.Column<bool>(type: "boolean", nullable: false),
                    LastUpdate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdateFailed = table.Column<bool>(type: "boolean", nullable: false),
                    EggIncId = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<string>(type: "text", nullable: true),
                    user_name = table.Column<string>(type: "text", nullable: true),
                    LastBackup = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    eggs_of_prophecy = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    soul_eggs = table.Column<double>(type: "double precision", nullable: false),
                    earnings_bonus = table.Column<double>(type: "double precision", nullable: false),
                    lifetime_cash_earned = table.Column<double>(type: "double precision", nullable: false),
                    DegreeOfSeperation = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalLeaderboardUsers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Guilds",
                columns: table => new
                {
                    Id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    ActiveElites = table.Column<string>(type: "text", nullable: true),
                    InactiveElites = table.Column<string>(type: "text", nullable: true),
                    ActiveStandards = table.Column<string>(type: "text", nullable: true),
                    InactiveStandards = table.Column<string>(type: "text", nullable: true),
                    DiscordSeverId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    OverflowServersJson = table.Column<string>(type: "text", nullable: true),
                    CoopNamePrefix = table.Column<string>(type: "text", nullable: true),
                    StaffCoopsMessageDetails = table.Column<string>(type: "text", nullable: true),
                    LeaderboardImage = table.Column<string>(type: "text", nullable: true),
                    CoopCategories = table.Column<string>(type: "text", nullable: true),
                    FinishedCategories = table.Column<string>(type: "text", nullable: true),
                    MinimumRunningScore = table.Column<float>(type: "real", nullable: false),
                    AddOutsideCoops = table.Column<bool>(type: "boolean", nullable: false),
                    _coopSettingsJson = table.Column<string>(type: "text", nullable: true),
                    _eventCustomizationsJson = table.Column<string>(type: "text", nullable: true),
                    _faqTopicsJson = table.Column<string>(type: "text", nullable: true),
                    FAQTopicsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    FAQTopicCooldownMinutes = table.Column<int>(type: "integer", nullable: false),
                    _channelDetailsJson = table.Column<string>(type: "text", nullable: true),
                    RolesToSync = table.Column<string>(type: "text", nullable: true),
                    DisableBG = table.Column<bool>(type: "boolean", nullable: false),
                    AllowGuilds = table.Column<bool>(type: "boolean", nullable: false),
                    GroupRoles = table.Column<string>(type: "text", nullable: true),
                    PublicScoreGrid = table.Column<bool>(type: "boolean", nullable: false),
                    RemoveFindCoopSpot = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Guilds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NasaApods",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Url = table.Column<string>(type: "text", nullable: true),
                    HdUrl = table.Column<string>(type: "text", nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "text", nullable: true),
                    MediaType = table.Column<string>(type: "text", nullable: true),
                    DateString = table.Column<string>(type: "text", nullable: true, defaultValueSql: "TO_CHAR(NOW() AT TIME ZONE 'UTC', 'YYYY-MM-DD')"),
                    Explanation = table.Column<string>(type: "text", nullable: true),
                    Copyright = table.Column<string>(type: "text", nullable: true),
                    _postedToBytes = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NasaApods", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "ResearchCostSubmissions",
                columns: table => new
                {
                    ID = table.Column<string>(type: "text", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Cost = table.Column<double>(type: "double precision", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchCostSubmissions", x => new { x.ID, x.Level, x.UserId });
                });

            migrationBuilder.CreateTable(
                name: "TemporaryRoles",
                columns: table => new
                {
                    UserId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RoleId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Expires = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    IsRemoved = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemporaryRoles", x => new { x.UserId, x.RoleId, x.Created });
                });

            migrationBuilder.CreateTable(
                name: "UserCsHistoryEntries",
                columns: table => new
                {
                    ContractIdentifier = table.Column<string>(type: "text", nullable: false),
                    CoopIdentifier = table.Column<string>(type: "text", nullable: false),
                    EggIncId = table.Column<string>(type: "text", nullable: false),
                    Cxp = table.Column<double>(type: "double precision", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCsHistoryEntries", x => new { x.CoopIdentifier, x.ContractIdentifier, x.EggIncId });
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DiscordId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    DiscordUsername = table.Column<string>(type: "text", nullable: true),
                    _eggIncIds = table.Column<string>(type: "text", nullable: true),
                    LastSleepingNotification = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GuildCoops = table.Column<int>(type: "integer", nullable: false),
                    GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    LastGuild = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    AcceptedRules = table.Column<bool>(type: "boolean", nullable: false),
                    DMSBlocked = table.Column<bool>(type: "boolean", nullable: false),
                    TempDisabled = table.Column<bool>(type: "boolean", nullable: false),
                    showEB = table.Column<bool>(type: "boolean", nullable: false),
                    OnBreakSince = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SkipNoPE = table.Column<bool>(type: "boolean", nullable: false),
                    SkipNoArtifacts = table.Column<bool>(type: "boolean", nullable: false),
                    SkipNoPiggyDouble = table.Column<bool>(type: "boolean", nullable: false),
                    CustomCoopName = table.Column<string>(type: "text", nullable: true),
                    ExpireCustomCoopName = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    _CustomBackups = table.Column<byte[]>(type: "bytea", nullable: true),
                    DMOnShipReturn = table.Column<bool>(type: "boolean", nullable: false),
                    ShipReturnMinutes = table.Column<int>(type: "integer", nullable: false),
                    ShipReturnStillFuelingMinutes = table.Column<int>(type: "integer", nullable: false),
                    ShipReturnDMAfterFuel = table.Column<bool>(type: "boolean", nullable: false),
                    NextShipReturnDMDue = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    _shipDMsByte = table.Column<byte[]>(type: "bytea", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    _coopSettingByte = table.Column<byte[]>(type: "bytea", nullable: true),
                    NextBreakExpire = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastBackupCheck = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Banned = table.Column<bool>(type: "boolean", nullable: false),
                    ServersBannedFrom = table.Column<string>(type: "text", nullable: true),
                    Usernames = table.Column<string>(type: "text", nullable: true),
                    EIDs = table.Column<string>(type: "text", nullable: true),
                    LastFAQPosted = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    _contractRegistrationByte = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreateOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Registered = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSnapShots",
                columns: table => new
                {
                    Date = table.Column<DateTime>(type: "Date", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EggIncID = table.Column<string>(type: "text", nullable: false),
                    EggsOfProphecy = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    SoulEggs = table.Column<double>(type: "double precision", nullable: false),
                    EarningsBonus = table.Column<double>(type: "double precision", nullable: false),
                    Prestiges = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    EggsOfTruth = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSnapShots", x => new { x.UserId, x.Date, x.EggIncID });
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Coops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractID = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: true),
                    CurrentUsers = table.Column<int>(type: "integer", nullable: true),
                    MaxUsers = table.Column<int>(type: "integer", nullable: true),
                    JoinUsers = table.Column<int>(type: "integer", nullable: false),
                    CoopEnds = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CoopCompleted = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProjectedFinish = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProjectedToFinish = table.Column<bool>(type: "boolean", nullable: false),
                    Finished = table.Column<bool>(type: "boolean", nullable: false),
                    League = table.Column<long>(type: "bigint", nullable: false),
                    AnyLeague = table.Column<bool>(type: "boolean", nullable: false),
                    SuccessfullyStarted = table.Column<bool>(type: "boolean", nullable: false),
                    DiscordChannelId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    OverflowGuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    UpdateMessagesId = table.Column<string>(type: "text", nullable: true),
                    CreatorID = table.Column<string>(type: "text", nullable: true),
                    LastUpdateToChannel = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WarningForDeleteChannel = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedChannel = table.Column<bool>(type: "boolean", nullable: false),
                    FindChannelErrors = table.Column<long>(type: "bigint", nullable: false),
                    Group = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    AddedFromBackup = table.Column<bool>(type: "boolean", nullable: false),
                    ThreadID = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    ThreadParentChannel = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    ThreadArchived = table.Column<bool>(type: "boolean", nullable: false),
                    RolesAddedToThread = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PseudoExpired = table.Column<bool>(type: "boolean", nullable: false),
                    _StatusCompressed = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Coops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Coops_Contracts_ContractID",
                        column: x => x.ContractID,
                        principalTable: "Contracts",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "GuildContracts",
                columns: table => new
                {
                    ContractID = table.Column<string>(type: "text", nullable: false),
                    GuildID = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    League = table.Column<long>(type: "bigint", nullable: false),
                    DiscordChannelId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    WarningForDeleteChannel = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedChannel = table.Column<bool>(type: "boolean", nullable: false),
                    NumberOfCoops = table.Column<int>(type: "integer", nullable: false),
                    Starters = table.Column<string>(type: "text", nullable: true),
                    Skip = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    HasScores = table.Column<bool>(type: "boolean", nullable: false),
                    OutsideCoops = table.Column<string>(type: "text", nullable: true),
                    BoardingGroup = table.Column<int>(type: "integer", nullable: false),
                    CcOnly = table.Column<bool>(type: "boolean", nullable: false),
                    ReadyToScore = table.Column<bool>(type: "boolean", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildContracts", x => new { x.ContractID, x.GuildID, x.League });
                    table.ForeignKey(
                        name: "FK_GuildContracts_Contracts_ContractID",
                        column: x => x.ContractID,
                        principalTable: "Contracts",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UpcomingContracts",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    GuildID = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    TargetDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsLeggacy = table.Column<bool>(type: "boolean", nullable: false),
                    _userRegs = table.Column<byte[]>(type: "bytea", nullable: true),
                    ContractId = table.Column<string>(type: "text", nullable: true),
                    ChannelId = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UpcomingContracts", x => x.ID);
                    table.ForeignKey(
                        name: "FK_UpcomingContracts_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "Demerit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    When = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    Permanent = table.Column<bool>(type: "boolean", nullable: false),
                    ContractID = table.Column<string>(type: "text", nullable: true),
                    Details = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Demerit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Demerit_Users_AdminUserId",
                        column: x => x.AdminUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Demerit_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Donations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    When = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<float>(type: "real", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Donations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Donations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Merit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    When = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Merit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Merit_Users_AdminUserId",
                        column: x => x.AdminUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Merit_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserCoopStatuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CoopId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EggIncId = table.Column<string>(type: "text", nullable: true),
                    EggIncName = table.Column<string>(type: "text", nullable: true),
                    Total = table.Column<double>(type: "double precision", nullable: false),
                    Rate = table.Column<double>(type: "double precision", nullable: false),
                    SleepingWarning = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCoopStatuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserCoopStatuses_Coops_CoopId",
                        column: x => x.CoopId,
                        principalTable: "Coops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserCoopStatuses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserCoopXrefs",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CoopId = table.Column<Guid>(type: "uuid", nullable: false),
                    EggIncId = table.Column<string>(type: "text", nullable: false),
                    RefEggIncId = table.Column<string>(type: "text", nullable: true),
                    FixedUserName = table.Column<string>(type: "text", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    JoinedCoop = table.Column<bool>(type: "boolean", nullable: false),
                    WaitingOnStarter = table.Column<bool>(type: "boolean", nullable: false),
                    AddedToChannel = table.Column<bool>(type: "boolean", nullable: false),
                    Starter = table.Column<bool>(type: "boolean", nullable: false),
                    WasAssigned = table.Column<bool>(type: "boolean", nullable: false),
                    JoinWarning12h = table.Column<bool>(type: "boolean", nullable: false),
                    JoinWarning24h = table.Column<bool>(type: "boolean", nullable: false),
                    JoinWarning24TillFinish = table.Column<bool>(type: "boolean", nullable: false),
                    LastStatusTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SleepingWarningTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Joined = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    TimeCheatReported = table.Column<bool>(type: "boolean", nullable: false),
                    _lastStatusByte = table.Column<byte[]>(type: "bytea", nullable: true),
                    SleepingDiscordMessageID = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    HoursSleeping = table.Column<int>(type: "integer", nullable: false),
                    TotalHoursSleeping = table.Column<float>(type: "real", nullable: false),
                    SiloTimeHours = table.Column<float>(type: "real", nullable: true),
                    NoDemerit = table.Column<bool>(type: "boolean", nullable: false),
                    Score = table.Column<float>(type: "real", nullable: true),
                    RunningScore = table.Column<float>(type: "real", nullable: true),
                    SoulPower = table.Column<double>(type: "double precision", nullable: true),
                    OutsideCoop = table.Column<bool>(type: "boolean", nullable: false),
                    HasTachyonDeflector = table.Column<bool>(type: "boolean", nullable: false),
                    EquipedTachyonDeflector = table.Column<bool>(type: "boolean", nullable: false),
                    PingOnFull = table.Column<bool>(type: "boolean", nullable: false),
                    PingOnHighestEB = table.Column<bool>(type: "boolean", nullable: false),
                    PingOnFinished = table.Column<bool>(type: "boolean", nullable: false),
                    CoopFullWarning = table.Column<bool>(type: "boolean", nullable: false),
                    Group = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    GussetCheatDetected = table.Column<bool>(type: "boolean", nullable: false),
                    _sleepTrackingByte = table.Column<byte[]>(type: "bytea", nullable: true),
                    _coopSettingByte = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCoopXrefs", x => new { x.UserId, x.CoopId, x.EggIncId });
                    table.ForeignKey(
                        name: "FK_UserCoopXrefs_Coops_CoopId",
                        column: x => x.CoopId,
                        principalTable: "Coops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserCoopXrefs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Coops_ContractID",
                table: "Coops",
                column: "ContractID");

            migrationBuilder.CreateIndex(
                name: "IX_Coops_DiscordChannelId_ThreadArchived_CoopEnds_ThreadID",
                table: "Coops",
                columns: new[] { "DiscordChannelId", "ThreadArchived", "CoopEnds", "ThreadID" });

            migrationBuilder.CreateIndex(
                name: "IX_Coops_Status",
                table: "Coops",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Coops_ThreadID",
                table: "Coops",
                column: "ThreadID");

            migrationBuilder.CreateIndex(
                name: "IX_Coops_ThreadID_Created",
                table: "Coops",
                columns: new[] { "ThreadID", "Created" });

            migrationBuilder.CreateIndex(
                name: "IX_Demerit_AdminUserId",
                table: "Demerit",
                column: "AdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Demerit_UserId",
                table: "Demerit",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Donations_UserId",
                table: "Donations",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Merit_AdminUserId",
                table: "Merit",
                column: "AdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Merit_UserId",
                table: "Merit",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UpcomingContracts_ContractId",
                table: "UpcomingContracts",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCoopStatuses_CoopId",
                table: "UserCoopStatuses",
                column: "CoopId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCoopStatuses_UserId",
                table: "UserCoopStatuses",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCoopXrefs_CoopId",
                table: "UserCoopXrefs",
                column: "CoopId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCoopXrefs_CreatedOn_JoinedCoop",
                table: "UserCoopXrefs",
                columns: new[] { "CreatedOn", "JoinedCoop" });

            migrationBuilder.CreateIndex(
                name: "IX_UserCoopXrefs_JoinedCoop",
                table: "UserCoopXrefs",
                column: "JoinedCoop");

            migrationBuilder.CreateIndex(
                name: "IX_UserCoopXrefs_JoinedCoop_CreatedOn",
                table: "UserCoopXrefs",
                columns: new[] { "JoinedCoop", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_UserCoopXrefs_UserId_JoinedCoop",
                table: "UserCoopXrefs",
                columns: new[] { "UserId", "JoinedCoop" });

            migrationBuilder.CreateIndex(
                name: "IX_UserCsHistoryEntries_ContractIdentifier",
                table: "UserCsHistoryEntries",
                column: "ContractIdentifier");

            migrationBuilder.CreateIndex(
                name: "IX_Users_DiscordId",
                table: "Users",
                column: "DiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_LastBackupCheck",
                table: "Users",
                column: "LastBackupCheck");

            migrationBuilder.CreateIndex(
                name: "IX_Users_LastModified",
                table: "Users",
                column: "LastModified");

            migrationBuilder.CreateIndex(
                name: "IX_Guilds_DiscordSeverId",
                table: "Guilds",
                column: "DiscordSeverId");

            migrationBuilder.CreateIndex(
                name: "IX_GuildContracts_DiscordChannelId",
                table: "GuildContracts",
                column: "DiscordChannelId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AutomationLogs");

            migrationBuilder.DropTable(
                name: "CustomEggs");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "Demerit");

            migrationBuilder.DropTable(
                name: "Donations");

            migrationBuilder.DropTable(
                name: "EventCustomizations");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "ExpiringShells");

            migrationBuilder.DropTable(
                name: "FAQTopics");

            migrationBuilder.DropTable(
                name: "GlobalLeaderboardCoops");

            migrationBuilder.DropTable(
                name: "GlobalLeaderboardUsers");

            migrationBuilder.DropTable(
                name: "GuildContracts");

            migrationBuilder.DropTable(
                name: "Guilds");

            migrationBuilder.DropTable(
                name: "Merit");

            migrationBuilder.DropTable(
                name: "NasaApods");

            migrationBuilder.DropTable(
                name: "ResearchCostSubmissions");

            migrationBuilder.DropTable(
                name: "TemporaryRoles");

            migrationBuilder.DropTable(
                name: "UpcomingContracts");

            migrationBuilder.DropTable(
                name: "UserCoopStatuses");

            migrationBuilder.DropTable(
                name: "UserCoopXrefs");

            migrationBuilder.DropTable(
                name: "UserCsHistoryEntries");

            migrationBuilder.DropTable(
                name: "UserSnapShots");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Coops");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Contracts");
        }
    }
}
