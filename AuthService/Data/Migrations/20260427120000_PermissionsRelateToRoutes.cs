using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Data.Migrations
{
    /// <inheritdoc />
    public partial class PermissionsRelateToRoutes : Migration
    {
        private static readonly string[] RoutesSystemIdCodeColumns = { "SystemId", "Code" };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sistema ainda não foi para produção: limpamos os vínculos antes de trocar a FK pra evitar
            // backfill de RouteId em linhas órfãs.
            migrationBuilder.Sql("DELETE FROM \"UserPermissions\";");
            migrationBuilder.Sql("DELETE FROM \"RolePermissions\";");
            migrationBuilder.Sql("DELETE FROM \"Permissions\";");

            migrationBuilder.DropForeignKey(
                name: "FK_Permissions_Systems_SystemId",
                table: "Permissions");

            migrationBuilder.DropIndex(
                name: "IX_Permissions_SystemId",
                table: "Permissions");

            migrationBuilder.DropColumn(
                name: "SystemId",
                table: "Permissions");

            migrationBuilder.AddColumn<System.Guid>(
                name: "RouteId",
                table: "Permissions",
                type: "uuid",
                nullable: false,
                defaultValue: System.Guid.Empty);

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_RouteId",
                table: "Permissions",
                column: "RouteId");

            migrationBuilder.AddForeignKey(
                name: "FK_Permissions_Routes_RouteId",
                table: "Permissions",
                column: "RouteId",
                principalTable: "Routes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Substituído pelo composto (SystemId, Code) — convenção do EF Core suprime o automático
            // de FK quando há índice começando pela coluna.
            migrationBuilder.DropIndex(
                name: "IX_Routes_SystemId",
                table: "Routes");

            migrationBuilder.CreateIndex(
                name: "UX_Routes_SystemId_Code",
                table: "Routes",
                columns: RoutesSystemIdCodeColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Routes_SystemId_Code",
                table: "Routes");

            migrationBuilder.CreateIndex(
                name: "IX_Routes_SystemId",
                table: "Routes",
                column: "SystemId");

            migrationBuilder.DropForeignKey(
                name: "FK_Permissions_Routes_RouteId",
                table: "Permissions");

            migrationBuilder.DropIndex(
                name: "IX_Permissions_RouteId",
                table: "Permissions");

            migrationBuilder.DropColumn(
                name: "RouteId",
                table: "Permissions");

            migrationBuilder.AddColumn<System.Guid>(
                name: "SystemId",
                table: "Permissions",
                type: "uuid",
                nullable: false,
                defaultValue: System.Guid.Empty);

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_SystemId",
                table: "Permissions",
                column: "SystemId");

            migrationBuilder.AddForeignKey(
                name: "FK_Permissions_Systems_SystemId",
                table: "Permissions",
                column: "SystemId",
                principalTable: "Systems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
