using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleSystemIdAndDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Adiciona Description como NULL (campo opcional max 500).
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Roles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // 2) Adiciona SystemId como nullable (transitório) para permitir backfill em bases pré-existentes
            //    sem violar NOT NULL antes da population.
            migrationBuilder.AddColumn<Guid>(
                name: "SystemId",
                table: "Roles",
                type: "uuid",
                nullable: true);

            // 3) Backfill: roles pré-existentes apontam para o sistema 'authenticator'. Idempotente:
            //    se o sistema 'authenticator' não existir (cenário improvável já que SystemSeeder roda
            //    no startup), o UPDATE não atualiza nenhuma linha e o ALTER NOT NULL adiante falhará,
            //    sinalizando o erro. Em bases novas (sem roles), o UPDATE é no-op e a sequência é segura.
            migrationBuilder.Sql(@"
                UPDATE ""Roles""
                SET ""SystemId"" = (SELECT ""Id"" FROM ""Systems"" WHERE ""Code"" = 'authenticator' LIMIT 1)
                WHERE ""SystemId"" IS NULL;
            ");

            // 4) Promove SystemId para NOT NULL após o backfill.
            migrationBuilder.AlterColumn<Guid>(
                name: "SystemId",
                table: "Roles",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            // 5) Drop do índice único global antigo (UX_Roles_Code) — substituído pelo escopo (SystemId, Code).
            migrationBuilder.DropIndex(
                name: "UX_Roles_Code",
                table: "Roles");

            // 6) Index simples por SystemId para acelerar lookups por sistema.
            migrationBuilder.CreateIndex(
                name: "IX_Roles_SystemId",
                table: "Roles",
                column: "SystemId");

            // 7) Unique scoped: (SystemId, Code) — Code passa a ser único POR sistema.
            migrationBuilder.CreateIndex(
                name: "UX_Roles_SystemId_Code",
                table: "Roles",
                columns: new[] { "SystemId", "Code" },
                unique: true);

            // 8) FK com onDelete: Restrict (não permitir apagar Sistema referenciado por roles).
            migrationBuilder.AddForeignKey(
                name: "FK_Roles_Systems_SystemId",
                table: "Roles",
                column: "SystemId",
                principalTable: "Systems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverte na ordem inversa.
            migrationBuilder.DropForeignKey(
                name: "FK_Roles_Systems_SystemId",
                table: "Roles");

            migrationBuilder.DropIndex(
                name: "UX_Roles_SystemId_Code",
                table: "Roles");

            migrationBuilder.DropIndex(
                name: "IX_Roles_SystemId",
                table: "Roles");

            // Recria o índice único global anterior.
            migrationBuilder.CreateIndex(
                name: "UX_Roles_Code",
                table: "Roles",
                column: "Code",
                unique: true);

            migrationBuilder.DropColumn(
                name: "SystemId",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Roles");
        }
    }
}
