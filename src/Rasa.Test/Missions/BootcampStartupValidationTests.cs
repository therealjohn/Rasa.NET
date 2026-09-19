using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using Rasa;
    using Rasa.Game;
    using Rasa.Managers;

    [TestClass]
    [DoNotParallelize]
    public class BootcampStartupValidationTests
    {
        [TestMethod]
        [DynamicData(nameof(RequiredContentFailureCases))]
        public void RequiredContentDefectsLogActionableStartupDiagnosticsAndBlockReadyState(
            string _,
            Action<MissionContentFixture> mutate,
            string expectedCode)
        {
            var fixture = MissionContentFixture.CreateValid();
            mutate(fixture);
            var snapshot = new MissionContentLoader().Load(fixture.CreateRepository());
            var report = new MissionContentValidator().Validate(
                snapshot,
                fixture.CreateWorldUnitOfWork());

            Assert.IsTrue(report.BlocksReadiness);
            CollectionAssert.Contains(
                report.Diagnostics.Select(diagnostic => diagnostic.Code).ToArray(),
                expectedCode);

            var output = CaptureLogs(() =>
            {
                Assert.IsFalse(Server.LogMissionValidationAndCheckReadiness(report));
            });

            foreach (var diagnostic in report.Diagnostics)
                StringAssert.Contains(output, diagnostic.ToOperatorMessage());
            StringAssert.Contains(
                output,
                "Mission content validation failed for required content; the Game server will not report ready.");
            Assert.IsFalse(output.Contains("Server ready!", StringComparison.Ordinal));
        }

        public static IEnumerable<object[]> RequiredContentFailureCases() =>
            MissionContentValidatorTests.GetFailureCases();

        private static string CaptureLogs(Action action)
        {
            Logger.UpdateConfig(new Logger.LoggerConfig
            {
                IsDebugMode = true,
                LogToFile = false,
                LogFilePath = null
            });

            var originalOut = Console.Out;
            using var writer = new StringWriter();
            Console.SetOut(writer);
            try
            {
                action();
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            return writer.ToString();
        }
    }
}
