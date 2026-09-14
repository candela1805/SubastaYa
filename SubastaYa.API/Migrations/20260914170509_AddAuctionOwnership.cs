using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubastaYa.API.Migrations
{
    /// <inheritdoc />
    public partial class AddAuctionOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VendedorId",
                table: "Subastas",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subastas_VendedorId",
                table: "Subastas",
                column: "VendedorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Subastas_Usuarios_VendedorId",
                table: "Subastas",
                column: "VendedorId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Subastas_Usuarios_VendedorId",
                table: "Subastas");

            migrationBuilder.DropIndex(
                name: "IX_Subastas_VendedorId",
                table: "Subastas");

            migrationBuilder.DropColumn(
                name: "VendedorId",
                table: "Subastas");
        }
    }
}
