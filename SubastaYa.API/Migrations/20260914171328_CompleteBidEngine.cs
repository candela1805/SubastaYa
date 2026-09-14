using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubastaYa.API.Migrations
{
    /// <inheritdoc />
    public partial class CompleteBidEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EsGanadora",
                table: "Pujas",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                ;WITH [RankedBids] AS
                (
                    SELECT
                        [Id],
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY [SubastaId]
                            ORDER BY [Monto] DESC, [FechaUtc] DESC, [Id] DESC
                        ) AS [Position]
                    FROM [Pujas]
                )
                UPDATE [Puja]
                SET [EsGanadora] =
                    CASE
                        WHEN [RankedBids].[Position] = 1 THEN CAST(1 AS bit)
                        ELSE CAST(0 AS bit)
                    END
                FROM [Pujas] AS [Puja]
                INNER JOIN [RankedBids]
                    ON [RankedBids].[Id] = [Puja].[Id];
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Pujas_SubastaId_EsGanadora",
                table: "Pujas",
                columns: new[] { "SubastaId", "EsGanadora" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pujas_SubastaId_EsGanadora",
                table: "Pujas");

            migrationBuilder.DropColumn(
                name: "EsGanadora",
                table: "Pujas");
        }
    }
}
