namespace PowerUp.Watcher
{
    public class CSharpWatcherOptions
    {
        [ConsoleInput("C# Input File", IsRequired = true, Position = 0, Example = "C:\\code.cs")]
        public string CSharpInput { get; set; }

        [ConsoleInput("X86 Assembly Output File", Position = 1, Example = "C:\\out.asm")]
        public string AsmOutput { get; set; }

        [ConsoleInput("IL Output File", Position = 2, Example = "C:\\out.il")]
        public string ILOutput { get; set; }

        [ConsoleInput("Lowered C# Output File", Position = 3, Example = "C:\\out.cs")]
        public string CSharpOutput { get; set; }
    }
}
