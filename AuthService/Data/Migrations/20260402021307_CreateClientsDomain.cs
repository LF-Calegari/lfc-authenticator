using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateClientsDomain : Migration
    {
        private const string UsersTable = "Users";
        private const string ClientsTable = "Clients";
        private const string ClientEmailsTable = "ClientEmails";
        private const string ClientPhonesTable = "ClientPhones";
        private const string ClientIdColumn = "ClientId";
        private const string UniqueIdentifierType = "uniqueidentifier";
        private const string DateTime2Type = "datetime2";
        private static readonly string[] ClientEmailsUniqueIndexColumns = { "ClientId", "Email" };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: ClientIdColumn,
                table: UsersTable,
                type: UniqueIdentifierType,
                nullable: true);

            migrationBuilder.CreateTable(
                name: ClientsTable,
                columns: table => new
                {
                    Id = table.Column<Guid>(type: UniqueIdentifierType, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    Cpf = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: true),
                    Cnpj = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: true),
                    CorporateName = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: DateTime2Type, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: DateTime2Type, nullable: false),
                    DeletedAt = table.Column<DateTime>(type: DateTime2Type, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: ClientEmailsTable,
                columns: table => new
                {
                    Id = table.Column<Guid>(type: UniqueIdentifierType, nullable: false),
                    ClientId = table.Column<Guid>(type: UniqueIdentifierType, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: DateTime2Type, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientEmails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientEmails_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: ClientsTable,
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: ClientPhonesTable,
                columns: table => new
                {
                    Id = table.Column<Guid>(type: UniqueIdentifierType, nullable: false),
                    ClientId = table.Column<Guid>(type: UniqueIdentifierType, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: DateTime2Type, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientPhones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientPhones_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: ClientsTable,
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_ClientId",
                table: UsersTable,
                column: ClientIdColumn);

            migrationBuilder.CreateIndex(
                name: "UX_ClientEmails_ClientId_Email",
                table: ClientEmailsTable,
                columns: ClientEmailsUniqueIndexColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ClientPhones_ClientId_Type_Number",
                table: ClientPhonesTable,
                columns: new[] { "ClientId", "Type", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clients_DeletedAt",
                table: ClientsTable,
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "UX_Clients_Cnpj",
                table: ClientsTable,
                column: "Cnpj",
                unique: true,
                filter: "[Cnpj] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Clients_Cpf",
                table: ClientsTable,
                column: "Cpf",
                unique: true,
                filter: "[Cpf] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Clients_ClientId",
                table: UsersTable,
                column: ClientIdColumn,
                principalTable: ClientsTable,
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Clients_ClientId",
                table: UsersTable);

            migrationBuilder.DropTable(
                name: ClientEmailsTable);

            migrationBuilder.DropTable(
                name: ClientPhonesTable);

            migrationBuilder.DropTable(
                name: ClientsTable);

            migrationBuilder.DropIndex(
                name: "IX_Users_ClientId",
                table: UsersTable);

            migrationBuilder.DropColumn(
                name: ClientIdColumn,
                table: UsersTable);
        }
    }
}
