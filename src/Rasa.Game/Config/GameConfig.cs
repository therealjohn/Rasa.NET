namespace Rasa.Config
{
    public class GameConfig
    {
        public const int DefaultTransferTimeoutSeconds = 60;

        public string PublicAddress { get; set; }
        public int Port { get; set; }
        public int Backlog { get; set; }
        public int TransferTimeoutSeconds { get; set; } = DefaultTransferTimeoutSeconds;
    }
}
