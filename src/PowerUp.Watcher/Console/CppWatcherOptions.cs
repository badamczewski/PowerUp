namespace PowerUp.Watcher
{
    public class CppWatcherOptions
    {
        [ConsoleInput("C++ Input File", IsRequired = true, Position = 0, Example = "C:\\code.cpp")]
        public string CppInput { get; set; }

        [ConsoleInput("X86 Assembly Output File", Position = 1, Example = "C:\\out.asm")]
        public string AsmOutput { get; set; }
    }
}
