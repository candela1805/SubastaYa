using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubastaYa.API.Migrations
{
    public partial class ProtectBusinessAuditImmutability : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_AuditoriaLogs_PreventUpdate
                ON dbo.AuditoriaLogs
                AFTER UPDATE
                AS
                BEGIN
                    SET NOCOUNT ON;

                    THROW 50002,
                        'Los registros de auditoria de negocio son inmutables.',
                        1;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER TR_AuditoriaLogs_PreventDelete
                ON dbo.AuditoriaLogs
                AFTER DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;

                    THROW 50003,
                        'Los registros de auditoria de negocio no pueden eliminarse.',
                        1;
                END;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS dbo.TR_AuditoriaLogs_PreventUpdate;
                """);

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS dbo.TR_AuditoriaLogs_PreventDelete;
                """);
        }
    }
}