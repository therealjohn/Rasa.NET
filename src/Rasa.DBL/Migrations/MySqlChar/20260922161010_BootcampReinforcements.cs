using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Rasa.Context.Char;
using Rasa.Services.Preloader;

namespace Rasa.Migrations.MySqlChar
{
    [DbContext(typeof(MySqlCharContext))]
    [Migration("20260922161010_BootcampReinforcements")]
    public class BootcampReinforcements : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) =>
            BootcampReinforcementsContent.UpCharacters(migrationBuilder);

        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Restore a character database backup to undo the survivor objective merge.");
    }
}
