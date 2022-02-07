using System;

namespace PowerUp.Watcher
{
    [AttributeUsage(AttributeTargets.Property)]
    public class ConsoleInputAttribute : Attribute
    {
        public string Name { get; set; }
        public bool IsRequired { get; set; }
        public int Position { get; set; }
        public string Example { get; set; }

        public ConsoleInputAttribute(string name, bool isRequired = false, int position = -1)
        {
            Name = name;
            IsRequired = isRequired;
            Position = position;
        }
    }
}
