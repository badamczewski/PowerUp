namespace PowerUp.Watcher
{
    public class RustWatcherOptions
    {
        [ConsoleInput("Rust Input File", IsRequired = true, Position = 0, Example = "C:\\code.rs")]
        public string RustInput { get; set; }

        [ConsoleInput("X86 Assembly Output File", Position = 1, Example = "C:\\out.asm")]
        public string AsmOutput { get; set; }
    }
}
