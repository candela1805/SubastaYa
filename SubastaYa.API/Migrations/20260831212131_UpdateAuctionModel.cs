using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubastaYa.API.Migrations
{
    /// <inheritdoc />
    public partial class UpdateAuctionModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Subastas_Activa",
                table: "Subastas");

            migrationBuilder.DropColumn(
                name: "Activa",
                table: "Subastas");

            migrationBuilder.AlterColumn<string>(
                name: "Descripcion",
                table: "Subastas",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Categoria",
                table: "Subastas",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Estado",
                table: "Subastas",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Programada");

            migrationBuilder.AddColumn<string>(
                name: "ImagenUrl",
                table: "Subastas",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IncrementoMinimo",
                table: "Subastas",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_Subastas_Categoria",
                table: "Subastas",
                column: "Categoria");

            migrationBuilder.CreateIndex(
                name: "IX_Subastas_Estado",
                table: "Subastas",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_Subastas_PrecioActual",
                table: "Subastas",
                column: "PrecioActual");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Subastas_Categoria",
                table: "Subastas");

            migrationBuilder.DropIndex(
                name: "IX_Subastas_Estado",
                table: "Subastas");

            migrationBuilder.DropIndex(
                name: "IX_Subastas_PrecioActual",
                table: "Subastas");

            migrationBuilder.DropColumn(
                name: "Categoria",
                table: "Subastas");

            migrationBuilder.DropColumn(
                name: "Estado",
                table: "Subastas");

            migrationBuilder.DropColumn(
                name: "ImagenUrl",
                table: "Subastas");

            migrationBuilder.DropColumn(
                name: "IncrementoMinimo",
                table: "Subastas");

            migrationBuilder.AlterColumn<string>(
                name: "Descripcion",
                table: "Subastas",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AddColumn<bool>(
                name: "Activa",
                table: "Subastas",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Subastas_Activa",
                table: "Subastas",
                column: "Activa");
        }
    }
}
