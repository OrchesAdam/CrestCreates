using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryManagement.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class R3_12_SettingHostNullUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "SettingValues"
                        WHERE "TenantId" IS NULL
                        GROUP BY "Name", "Scope", "ProviderKey"
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'R3-12: duplicate host-null SettingValues rows exist; resolve duplicates before applying the unique constraint.';
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SettingValues_Name_Scope_ProviderKey",
                table: "SettingValues",
                columns: new[] { "Name", "Scope", "ProviderKey" },
                unique: true,
                filter: "\"TenantId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SettingValues_Name_Scope_ProviderKey",
                table: "SettingValues");
        }
    }
}
