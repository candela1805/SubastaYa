using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubastaYa.API.Migrations
{
    /// <inheritdoc />
    public partial class HardenAuctionSettlementConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider ==
                "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(
                    """
                    IF EXISTS (
                        SELECT 1
                        FROM [Pujas]
                        WHERE [EsGanadora] = 1
                        GROUP BY [SubastaId]
                        HAVING COUNT(*) > 1)
                    BEGIN
                        THROW 51000, 'No se puede crear el indice de ganador unico: existen subastas con mas de una puja ganadora.', 1;
                    END;

                    IF EXISTS (
                        SELECT 1
                        FROM [TransaccionesLedger]
                        WHERE [Monto] <= 0)
                    BEGIN
                        THROW 51001, 'No se puede crear la restriccion del Ledger: existen movimientos con monto no positivo.', 1;
                    END;

                    IF EXISTS (
                        SELECT 1
                        FROM [LiquidacionesSubasta]
                        WHERE [ImporteFinal] <= 0)
                    BEGIN
                        THROW 51002, 'No se puede crear la restriccion de liquidaciones: existen importes finales no positivos.', 1;
                    END;
                    """);
            }

            migrationBuilder.DropForeignKey(
                name: "FK_TransaccionesLedger_Billeteras_BilleteraId",
                table: "TransaccionesLedger");

            migrationBuilder.DropIndex(
                name: "IX_TransaccionesLedger_LiquidacionSubastaId",
                table: "TransaccionesLedger");

            migrationBuilder.DropIndex(
                name: "IX_Pujas_SubastaId_EsGanadora",
                table: "Pujas");

            migrationBuilder.RenameIndex(
                name: "IX_LiquidacionesSubasta_SubastaId",
                table: "LiquidacionesSubasta",
                newName: "UX_LiquidacionesSubasta_SubastaId");

            migrationBuilder.RenameIndex(
                name: "IX_LiquidacionesSubasta_PujaGanadoraId",
                table: "LiquidacionesSubasta",
                newName: "UX_LiquidacionesSubasta_PujaGanadoraId");

            migrationBuilder.AddColumn<string>(
                name: "ClaveIdempotencia",
                table: "TransaccionesLedger",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaveIdempotencia",
                table: "AuditoriaLogs",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_TransaccionesLedger_ClaveIdempotencia",
                table: "TransaccionesLedger",
                column: "ClaveIdempotencia",
                unique: true,
                filter: "[ClaveIdempotencia] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_TransaccionesLedger_LiquidacionSubastaId_Tipo",
                table: "TransaccionesLedger",
                columns: new[] { "LiquidacionSubastaId", "Tipo" },
                unique: true,
                filter: "[LiquidacionSubastaId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransaccionesLedger_MontoPositivo",
                table: "TransaccionesLedger",
                sql: "[Monto] > 0");

            migrationBuilder.CreateIndex(
                name: "IX_Subastas_Estado_FechaFinUtc_Id",
                table: "Subastas",
                columns: new[] { "Estado", "FechaFinUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Subastas_Estado_FechaInicioUtc_Id",
                table: "Subastas",
                columns: new[] { "Estado", "FechaInicioUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_Pujas_SubastaId_Ganadora",
                table: "Pujas",
                column: "SubastaId",
                unique: true,
                filter: "[EsGanadora] = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LiquidacionesSubasta_ImporteFinalPositivo",
                table: "LiquidacionesSubasta",
                sql: "[ImporteFinal] > 0");

            migrationBuilder.CreateIndex(
                name: "UX_AuditoriaLogs_ClaveIdempotencia",
                table: "AuditoriaLogs",
                column: "ClaveIdempotencia",
                unique: true,
                filter: "[ClaveIdempotencia] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_TransaccionesLedger_Billeteras_BilleteraId",
                table: "TransaccionesLedger",
                column: "BilleteraId",
                principalTable: "Billeteras",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TransaccionesLedger_Billeteras_BilleteraId",
                table: "TransaccionesLedger");

            migrationBuilder.DropIndex(
                name: "UX_TransaccionesLedger_ClaveIdempotencia",
                table: "TransaccionesLedger");

            migrationBuilder.DropIndex(
                name: "UX_TransaccionesLedger_LiquidacionSubastaId_Tipo",
                table: "TransaccionesLedger");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TransaccionesLedger_MontoPositivo",
                table: "TransaccionesLedger");

            migrationBuilder.DropIndex(
                name: "IX_Subastas_Estado_FechaFinUtc_Id",
                table: "Subastas");

            migrationBuilder.DropIndex(
                name: "IX_Subastas_Estado_FechaInicioUtc_Id",
                table: "Subastas");

            migrationBuilder.DropIndex(
                name: "UX_Pujas_SubastaId_Ganadora",
                table: "Pujas");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LiquidacionesSubasta_ImporteFinalPositivo",
                table: "LiquidacionesSubasta");

            migrationBuilder.DropIndex(
                name: "UX_AuditoriaLogs_ClaveIdempotencia",
                table: "AuditoriaLogs");

            migrationBuilder.DropColumn(
                name: "ClaveIdempotencia",
                table: "TransaccionesLedger");

            migrationBuilder.DropColumn(
                name: "ClaveIdempotencia",
                table: "AuditoriaLogs");

            migrationBuilder.RenameIndex(
                name: "UX_LiquidacionesSubasta_SubastaId",
                table: "LiquidacionesSubasta",
                newName: "IX_LiquidacionesSubasta_SubastaId");

            migrationBuilder.RenameIndex(
                name: "UX_LiquidacionesSubasta_PujaGanadoraId",
                table: "LiquidacionesSubasta",
                newName: "IX_LiquidacionesSubasta_PujaGanadoraId");

            migrationBuilder.CreateIndex(
                name: "IX_TransaccionesLedger_LiquidacionSubastaId",
                table: "TransaccionesLedger",
                column: "LiquidacionSubastaId");

            migrationBuilder.CreateIndex(
                name: "IX_Pujas_SubastaId_EsGanadora",
                table: "Pujas",
                columns: new[] { "SubastaId", "EsGanadora" });

            migrationBuilder.AddForeignKey(
                name: "FK_TransaccionesLedger_Billeteras_BilleteraId",
                table: "TransaccionesLedger",
                column: "BilleteraId",
                principalTable: "Billeteras",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
