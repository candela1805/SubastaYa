using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubastaYa.API.Migrations
{
    /// <inheritdoc />
    public partial class AddBidsAndAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditoriaLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubastaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TipoEvento = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Detalle = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    FechaUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditoriaLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditoriaLogs_Subastas_SubastaId",
                        column: x => x.SubastaId,
                        principalTable: "Subastas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Pujas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubastaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Monto = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FechaUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pujas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pujas_Subastas_SubastaId",
                        column: x => x.SubastaId,
                        principalTable: "Subastas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pujas_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditoriaLogs_SubastaId_FechaUtc",
                table: "AuditoriaLogs",
                columns: new[] { "SubastaId", "FechaUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Pujas_SubastaId_FechaUtc",
                table: "Pujas",
                columns: new[] { "SubastaId", "FechaUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Pujas_UsuarioId",
                table: "Pujas",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditoriaLogs");

            migrationBuilder.DropTable(
                name: "Pujas");
        }
    }
}
