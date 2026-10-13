using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubastaYa.API.Migrations
{
    /// <inheritdoc />
    public partial class ExtendBusinessAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "SubastaId",
                table: "AuditoriaLogs",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "BilleteraId",
                table: "AuditoriaLogs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UsuarioId",
                table: "AuditoriaLogs",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
        IF EXISTS (
            SELECT 1
            FROM AuditoriaLogs
            WHERE SubastaId IS NULL
        )
        BEGIN
            THROW 50001,
                'No se puede revertir ExtendBusinessAudit: existen auditorias sin SubastaId.',
                1;
        END
        """);

            migrationBuilder.DropColumn(
                name: "BilleteraId",
                table: "AuditoriaLogs");

            migrationBuilder.DropColumn(
                name: "UsuarioId",
                table: "AuditoriaLogs");

            migrationBuilder.AlterColumn<Guid>(
                name: "SubastaId",
                table: "AuditoriaLogs",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
