using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Rasa.Context.World;
using Rasa.Services.Preloader;

namespace Rasa.Migrations.SqliteWorld
{
    [DbContext(typeof(SqliteWorldContext))]
    [Migration("20260922004500_BootcampCaptureTheFlag")]
    public class BootcampCaptureTheFlag : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) =>
            BootcampCaptureTheFlagContent.Up(migrationBuilder);

        protected override void Down(MigrationBuilder migrationBuilder) =>
            BootcampCaptureTheFlagContent.Down(migrationBuilder);
    }
}
