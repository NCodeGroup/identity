using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NCode.Identity.OpenId.GlobalDirectory.Host.Migrations.Directory
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Cells",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(31)", maxLength: 31, nullable: false),
                    Region = table.Column<string>(type: "character varying(31)", maxLength: 31, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ManagementUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsControlPlane = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cells", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "Changes",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    TeamId = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: true),
                    DocumentJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Changes", x => x.Sequence);
                });

            migrationBuilder.CreateTable(
                name: "Platforms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    DocumentJson = table.Column<string>(type: "text", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Platforms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RetiredIssuers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Issuer = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    NormalizedIssuer = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetiredIssuers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sequences",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TeamId = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    NormalizedTeamId = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    NormalizedParentTeamId = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: true),
                    NormalizedPath = table.Column<string>(type: "character varying(513)", maxLength: 513, nullable: false, collation: "C"),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DocumentJson = table.Column<string>(type: "text", nullable: true),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantMoveOffers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NormalizedTenantId = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    FromCell = table.Column<string>(type: "character varying(31)", maxLength: 31, nullable: false),
                    ToCell = table.Column<string>(type: "character varying(31)", maxLength: 31, nullable: false),
                    CreatedWhen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresWhen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantMoveOffers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    NormalizedTenantId = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    Issuer = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    NormalizedIssuer = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    NormalizedTeamId = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: true),
                    Cell = table.Column<string>(type: "character varying(31)", maxLength: 31, nullable: true),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedWhen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedWhen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RetiredIssuers_NormalizedIssuer",
                table: "RetiredIssuers",
                column: "NormalizedIssuer",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teams_NormalizedParentTeamId",
                table: "Teams",
                column: "NormalizedParentTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_NormalizedPath",
                table: "Teams",
                column: "NormalizedPath");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_NormalizedTeamId",
                table: "Teams",
                column: "NormalizedTeamId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantMoveOffers_NormalizedTenantId",
                table: "TenantMoveOffers",
                column: "NormalizedTenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_NormalizedIssuer",
                table: "Tenants",
                column: "NormalizedIssuer",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_NormalizedTeamId",
                table: "Tenants",
                column: "NormalizedTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_NormalizedTenantId",
                table: "Tenants",
                column: "NormalizedTenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cells");

            migrationBuilder.DropTable(
                name: "Changes");

            migrationBuilder.DropTable(
                name: "Platforms");

            migrationBuilder.DropTable(
                name: "RetiredIssuers");

            migrationBuilder.DropTable(
                name: "Sequences");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "TenantMoveOffers");

            migrationBuilder.DropTable(
                name: "Tenants");
        }
    }
}
