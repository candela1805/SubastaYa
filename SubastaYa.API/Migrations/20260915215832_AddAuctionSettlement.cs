using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubastaYa.API.Migrations
{
    /// <inheritdoc />
    public partial class AddAuctionSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LiquidacionSubastaId",
                table: "TransaccionesLedger",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LiquidacionesSubasta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubastaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PujaGanadoraId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompradorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VendedorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImporteFinal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FechaAdjudicacionUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FechaLiquidacionUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiquidacionesSubasta", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiquidacionesSubasta_Pujas_PujaGanadoraId",
                        column: x => x.PujaGanadoraId,
                        principalTable: "Pujas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiquidacionesSubasta_Subastas_SubastaId",
                        column: x => x.SubastaId,
                        principalTable: "Subastas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiquidacionesSubasta_Usuarios_CompradorId",
                        column: x => x.CompradorId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiquidacionesSubasta_Usuarios_VendedorId",
                        column: x => x.VendedorId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransaccionesLedger_LiquidacionSubastaId",
                table: "TransaccionesLedger",
                column: "LiquidacionSubastaId");

            migrationBuilder.CreateIndex(
                name: "IX_LiquidacionesSubasta_CompradorId",
                table: "LiquidacionesSubasta",
                column: "CompradorId");

            migrationBuilder.CreateIndex(
                name: "IX_LiquidacionesSubasta_PujaGanadoraId",
                table: "LiquidacionesSubasta",
                column: "PujaGanadoraId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiquidacionesSubasta_SubastaId",
                table: "LiquidacionesSubasta",
                column: "SubastaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiquidacionesSubasta_VendedorId",
                table: "LiquidacionesSubasta",
                column: "VendedorId");

            migrationBuilder.AddForeignKey(
                name: "FK_TransaccionesLedger_LiquidacionesSubasta_LiquidacionSubastaId",
                table: "TransaccionesLedger",
                column: "LiquidacionSubastaId",
                principalTable: "LiquidacionesSubasta",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TransaccionesLedger_LiquidacionesSubasta_LiquidacionSubastaId",
                table: "TransaccionesLedger");

            migrationBuilder.DropTable(
                name: "LiquidacionesSubasta");

            migrationBuilder.DropIndex(
                name: "IX_TransaccionesLedger_LiquidacionSubastaId",
                table: "TransaccionesLedger");

            migrationBuilder.DropColumn(
                name: "LiquidacionSubastaId",
                table: "TransaccionesLedger");
        }
    }
}
