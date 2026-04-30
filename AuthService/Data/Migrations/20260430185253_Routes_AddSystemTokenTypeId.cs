using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Data.Migrations
{
    /// <inheritdoc />
    public partial class Routes_AddSystemTokenTypeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Adiciona a coluna como nullable (transitório) para permitir backfill em bases pré-existentes
            //    sem violar NOT NULL antes da population.
            migrationBuilder.AddColumn<Guid>(
                name: "SystemTokenTypeId",
                table: "Routes",
                type: "uuid",
                nullable: true);

            // 2) Garante que existe pelo menos um SystemTokenType canônico com Code='default' para servir
            //    como referência do backfill. Idempotente via ON CONFLICT — não recria se já existir
            //    (seeder pode ter rodado, ou outra migration/processo já garantiu).
            migrationBuilder.Sql(@"
                INSERT INTO ""SystemTokenTypes"" (""Id"", ""Name"", ""Code"", ""Description"", ""CreatedAt"", ""UpdatedAt"", ""DeletedAt"")
                VALUES (
                    gen_random_uuid(),
                    'Default',
                    'default',
                    'Política JWT padrão para rotas autenticadas (Bearer JWT do usuário corrente).',
                    now() AT TIME ZONE 'UTC',
                    now() AT TIME ZONE 'UTC',
                    NULL
                )
                ON CONFLICT (""Code"") DO NOTHING;
            ");

            // 3) Backfill: rotas pré-existentes apontam para o SystemTokenType 'default'.
            migrationBuilder.Sql(@"
                UPDATE ""Routes""
                SET ""SystemTokenTypeId"" = (SELECT ""Id"" FROM ""SystemTokenTypes"" WHERE ""Code"" = 'default' LIMIT 1)
                WHERE ""SystemTokenTypeId"" IS NULL;
            ");

            // 4) Promove para NOT NULL após o backfill.
            migrationBuilder.AlterColumn<Guid>(
                name: "SystemTokenTypeId",
                table: "Routes",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            // 5) Index e FK com onDelete: Restrict (não permitir apagar SystemTokenType referenciado por rotas).
            migrationBuilder.CreateIndex(
                name: "IX_Routes_SystemTokenTypeId",
                table: "Routes",
                column: "SystemTokenTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Routes_SystemTokenTypes_SystemTokenTypeId",
                table: "Routes",
                column: "SystemTokenTypeId",
                principalTable: "SystemTokenTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverte na ordem inversa. NÃO apaga registros de SystemTokenTypes — eles podem estar em uso
            // por outros consumidores pós-down (semântica de "release" da coluna, não do catálogo).
            migrationBuilder.DropForeignKey(
                name: "FK_Routes_SystemTokenTypes_SystemTokenTypeId",
                table: "Routes");

            migrationBuilder.DropIndex(
                name: "IX_Routes_SystemTokenTypeId",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "SystemTokenTypeId",
                table: "Routes");
        }
    }
}
