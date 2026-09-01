using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubastaYa.API.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletBalanceConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Billeteras_RetenidoNoSuperaTotal",
                table: "Billeteras",
                sql: "[SaldoRetenido] <= [SaldoTotal]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Billeteras_SaldoDisponibleConsistente",
                table: "Billeteras",
                sql: "[SaldoDisponible] = [SaldoTotal] - [SaldoRetenido]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Billeteras_SaldosNoNegativos",
                table: "Billeteras",
                sql: "[SaldoTotal] >= 0 AND [SaldoRetenido] >= 0 AND [SaldoDisponible] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Billeteras_RetenidoNoSuperaTotal",
                table: "Billeteras");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Billeteras_SaldoDisponibleConsistente",
                table: "Billeteras");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Billeteras_SaldosNoNegativos",
                table: "Billeteras");
        }
    }
}
