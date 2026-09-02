using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SubastaYa.API.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditoriaLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Entidad = table.Column<string>(type: "TEXT", nullable: false),
                    EntidadId = table.Column<int>(type: "INTEGER", nullable: true),
                    Accion = table.Column<string>(type: "TEXT", nullable: false),
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: true),
                    DatalleJson = table.Column<string>(type: "TEXT", nullable: false),
                    Fecha = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditoriaLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Billeteras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    SaldoTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    SaldoRetenido = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Version = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Billeteras", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categorias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nombre = table.Column<string>(type: "TEXT", nullable: false),
                    UrlIcono = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categorias", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Pujas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SubastaId = table.Column<int>(type: "INTEGER", nullable: false),
                    CompradorId = table.Column<int>(type: "INTEGER", nullable: false),
                    Monto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FechaPuja = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pujas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subastas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VendedorId = table.Column<int>(type: "INTEGER", nullable: false),
                    CategoriaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Titulo = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Descripcion = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    UrlImagen = table.Column<string>(type: "TEXT", nullable: true),
                    PrecioBase = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    IncrementoMinimo = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    FechaInicio = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FechaFin = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Version = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subastas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransaccionesLedger",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BilleteraId = table.Column<int>(type: "INTEGER", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", nullable: false),
                    Monto = table.Column<decimal>(type: "decimal(10,2)", precision: 18, scale: 2, nullable: false),
                    Fecha = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SubastaId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransaccionesLedger", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    Nombre = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    FechaRegistro = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Billeteras",
                columns: new[] { "Id", "SaldoRetenido", "SaldoTotal", "UsuarioId", "Version" },
                values: new object[,]
                {
                    { 1, 0m, 0m, 1, new byte[] { 1 } },
                    { 2, 45000m, 150000m, 2, new byte[] { 2 } },
                    { 3, 0m, 200000m, 3, new byte[] { 3 } },
                    { 4, 0m, 500m, 4, new byte[] { 4 } }
                });

            migrationBuilder.InsertData(
                table: "Categorias",
                columns: new[] { "Id", "Nombre", "UrlIcono" },
                values: new object[,]
                {
                    { 1, "Tecnología", "tech.png" },
                    { 2, "Coleccionables", "col.png" },
                    { 3, "Indumentaria", "ind.png" },
                    { 4, "Vehículos", "veh.png" }
                });

            migrationBuilder.InsertData(
                table: "Pujas",
                columns: new[] { "Id", "CompradorId", "FechaPuja", "Monto", "SubastaId" },
                values: new object[,]
                {
                    { 1, 3, new DateTime(2025, 1, 1, 12, 10, 0, 0, DateTimeKind.Utc), 35000m, 1 },
                    { 2, 2, new DateTime(2025, 1, 1, 12, 20, 0, 0, DateTimeKind.Utc), 45000m, 1 }
                });

            migrationBuilder.InsertData(
                table: "Subastas",
                columns: new[] { "Id", "CategoriaId", "Descripcion", "Estado", "FechaFin", "FechaInicio", "IncrementoMinimo", "PrecioBase", "Titulo", "UrlImagen", "VendedorId", "Version" },
                values: new object[,]
                {
                    { 1, 1, "Usada", "ACTIVA", new DateTime(2030, 1, 1, 12, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 12, 0, 0, 0, DateTimeKind.Utc), 5000m, 30000m, "MacBook Pro", "https://images.unsplash.com/photo-1517336714731-489689fd1ca8?auto=format&fit=crop&w=600", 1, new byte[] { 1 } },
                    { 2, 2, "Colección", "ACTIVA", new DateTime(2030, 1, 1, 12, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 12, 0, 0, 0, DateTimeKind.Utc), 1000m, 10000m, "Reloj Antiguo", "https://images.unsplash.com/photo-1524592094714-0f0654e20314?auto=format&fit=crop&w=600", 1, new byte[] { 2 } },
                    { 3, 3, "Nuevas", "PROGRAMADA", new DateTime(2031, 1, 2, 12, 0, 0, 0, DateTimeKind.Utc), new DateTime(2031, 1, 1, 12, 0, 0, 0, DateTimeKind.Utc), 2000m, 50000m, "Zapatillas Jordan", "https://images.unsplash.com/photo-1542291026-7eec264c27ff?auto=format&fit=crop&w=600", 1, new byte[] { 3 } },
                    { 4, 1, "24 pulgadas", "FINALIZADA", new DateTime(2024, 12, 31, 12, 0, 0, 0, DateTimeKind.Utc), new DateTime(2024, 12, 30, 12, 0, 0, 0, DateTimeKind.Utc), 1000m, 15000m, "Monitor LG", "https://images.unsplash.com/photo-1527443224154-c4a3942d3acf?auto=format&fit=crop&w=600", 1, new byte[] { 4 } },
                    { 5, 4, "Playera", "DESIERTA", new DateTime(2024, 12, 30, 12, 0, 0, 0, DateTimeKind.Utc), new DateTime(2024, 12, 29, 12, 0, 0, 0, DateTimeKind.Utc), 5000m, 80000m, "Bicicleta", "https://images.unsplash.com/photo-1571068316344-75bc76f77890?auto=format&fit=crop&w=600", 1, new byte[] { 5 } }
                });

            migrationBuilder.InsertData(
                table: "TransaccionesLedger",
                columns: new[] { "Id", "BilleteraId", "Fecha", "Monto", "SubastaId", "Tipo" },
                values: new object[,]
                {
                    { 1, 2, new DateTime(2024, 12, 31, 12, 0, 0, 0, DateTimeKind.Utc), 150000m, null, "DEPOSITO" },
                    { 2, 3, new DateTime(2024, 12, 31, 12, 0, 0, 0, DateTimeKind.Utc), 200000m, null, "DEPOSITO" },
                    { 3, 4, new DateTime(2024, 12, 31, 12, 0, 0, 0, DateTimeKind.Utc), 500m, null, "DEPOSITO" },
                    { 4, 2, new DateTime(2025, 1, 1, 12, 20, 0, 0, DateTimeKind.Utc), 45000m, 1, "RETENCION" }
                });

            migrationBuilder.InsertData(
                table: "Usuarios",
                columns: new[] { "Id", "Email", "FechaRegistro", "Nombre", "PasswordHash" },
                values: new object[,]
                {
                    { 1, "vendedor@test.com", new DateTime(2025, 1, 1, 12, 0, 0, 0, DateTimeKind.Utc), "Vendedor", "hash123" },
                    { 2, "comprador1@test.com", new DateTime(2025, 1, 1, 12, 0, 0, 0, DateTimeKind.Utc), "Comprador 1", "hash123" },
                    { 3, "comprador2@test.com", new DateTime(2025, 1, 1, 12, 0, 0, 0, DateTimeKind.Utc), "Comprador 2", "hash123" },
                    { 4, "sinfondos@test.com", new DateTime(2025, 1, 1, 12, 0, 0, 0, DateTimeKind.Utc), "Sin Fondos", "hash123" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subastas_Estado",
                table: "Subastas",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_Subastas_FechaFin",
                table: "Subastas",
                column: "FechaFin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditoriaLogs");

            migrationBuilder.DropTable(
                name: "Billeteras");

            migrationBuilder.DropTable(
                name: "Categorias");

            migrationBuilder.DropTable(
                name: "Pujas");

            migrationBuilder.DropTable(
                name: "Subastas");

            migrationBuilder.DropTable(
                name: "TransaccionesLedger");

            migrationBuilder.DropTable(
                name: "Usuarios");
        }
    }
}
