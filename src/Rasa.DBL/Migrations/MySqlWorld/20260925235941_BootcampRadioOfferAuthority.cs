using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.MySqlWorld
{
    /// <inheritdoc />
    public partial class BootcampRadioOfferAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
            => Services.Preloader.Missions.BootcampRadioOffersV1.Up(migrationBuilder);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new System.NotSupportedException("Bootcamp radio authority is forward-only; restore a backup rather than silently removing its source.");
    }
}
