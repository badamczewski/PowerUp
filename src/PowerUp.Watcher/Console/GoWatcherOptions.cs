namespace PowerUp.Watcher
{
    public class GoWatcherOptions
    {
        [ConsoleInput("GO Input File", IsRequired = true, Position = 0, Example = "C:\\code.go")]
        public string GOInput { get; set; }

        [ConsoleInput("GO Assembly Output File", Position = 1, Example = "C:\\out.asm")]
        public string AsmOutput { get; set; }
    }
}
