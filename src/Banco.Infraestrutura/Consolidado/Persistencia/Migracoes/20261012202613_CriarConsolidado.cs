using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banco.Infraestrutura.Consolidado.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class CriarConsolidado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lancamentos_projetados",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conta_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tipo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    valor = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ocorrido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lancamentos_projetados", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "marcas_atualizacao",
                columns: table => new
                {
                    conta_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ultimo_ocorrido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_marcas_atualizacao", x => x.conta_id);
                });

            migrationBuilder.CreateTable(
                name: "mensagens_processadas",
                columns: table => new
                {
                    id_mensagem = table.Column<Guid>(type: "uuid", nullable: false),
                    processado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mensagens_processadas", x => x.id_mensagem);
                });

            migrationBuilder.CreateTable(
                name: "resumos_diarios",
                columns: table => new
                {
                    conta_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dia = table.Column<DateOnly>(type: "date", nullable: false),
                    creditos = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    debitos = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resumos_diarios", x => new { x.conta_id, x.dia });
                });

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_projetados_conta_ocorrido_em",
                table: "lancamentos_projetados",
                columns: new[] { "conta_id", "ocorrido_em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lancamentos_projetados");

            migrationBuilder.DropTable(
                name: "marcas_atualizacao");

            migrationBuilder.DropTable(
                name: "mensagens_processadas");

            migrationBuilder.DropTable(
                name: "resumos_diarios");
        }
    }
}
